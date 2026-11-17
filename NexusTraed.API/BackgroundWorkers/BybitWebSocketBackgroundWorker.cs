using System.Net.WebSockets;
using System.Text;
using Microsoft.AspNetCore.SignalR;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NexusTraed.API.DTOs;
using NexusTraed.API.Hubs;

namespace NexusTraed.API.BackgroundWorkers;

/// <summary>
/// Connects to Bybit Testnet WebSocket public stream, subscribes to kline data,
/// computes ATR in real-time, and broadcasts updates via SignalR.
/// </summary>
public class BybitWebSocketBackgroundWorker : BackgroundService
{
    private const string WsUrl = "wss://stream-testnet.bybit.com/v5/public/linear";
    private const int AtrPeriod = 14;

    // Symbols to track (can be extended or made config-driven)
    private static readonly string[] TrackedSymbols = ["BTCUSDT", "ETHUSDT"];

    private readonly IHubContext<TradingHub> _hub;
    private readonly ILogger<BybitWebSocketBackgroundWorker> _logger;

    // Per-symbol sliding window of candle data for ATR calculation
    private readonly Dictionary<string, Queue<KlineCandle>> _candleWindows = [];

    public BybitWebSocketBackgroundWorker(
        IHubContext<TradingHub> hub,
        ILogger<BybitWebSocketBackgroundWorker> logger)
    {
        _hub = hub;
        _logger = logger;

        foreach (var sym in TrackedSymbols)
            _candleWindows[sym] = new Queue<KlineCandle>(AtrPeriod + 1);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ConnectAndStreamAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "WebSocket error. Reconnecting in 5s...");
                await Task.Delay(5_000, stoppingToken);
            }
        }
    }

    private async Task ConnectAndStreamAsync(CancellationToken ct)
    {
        using var ws = new ClientWebSocket();
        ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);

        _logger.LogInformation("Connecting to Bybit WebSocket: {Url}", WsUrl);
        await ws.ConnectAsync(new Uri(WsUrl), ct);
        _logger.LogInformation("Connected to Bybit WebSocket.");

        // Subscribe to 1-minute klines for all tracked symbols
        await SubscribeAsync(ws, ct);

        // Start heartbeat ping
        _ = HeartbeatAsync(ws, ct);

        var buffer = new byte[32768];

        while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
        {
            var result = await ws.ReceiveAsync(buffer, ct);

            if (result.MessageType == WebSocketMessageType.Close)
            {
                _logger.LogWarning("WebSocket closed by server.");
                break;
            }

            var json = Encoding.UTF8.GetString(buffer, 0, result.Count);
            await ProcessMessageAsync(json);
        }
    }

    private async Task SubscribeAsync(ClientWebSocket ws, CancellationToken ct)
    {
        var topics = TrackedSymbols
            .Select(s => $"kline.1.{s}")
            .ToList();

        var subscribeMsg = JsonConvert.SerializeObject(new
        {
            op = "subscribe",
            args = topics
        });

        var bytes = Encoding.UTF8.GetBytes(subscribeMsg);
        await ws.SendAsync(bytes, WebSocketMessageType.Text, true, ct);
        _logger.LogInformation("Subscribed to: {Topics}", string.Join(", ", topics));
    }

    private async Task HeartbeatAsync(ClientWebSocket ws, CancellationToken ct)
    {
        var ping = Encoding.UTF8.GetBytes("{\"op\":\"ping\"}");
        while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
        {
            await Task.Delay(20_000, ct);
            if (ws.State != WebSocketState.Open) break;

            var sw = System.Diagnostics.Stopwatch.StartNew();
            await ws.SendAsync(ping, WebSocketMessageType.Text, true, ct);
            sw.Stop();

            await _hub.Clients.All.SendAsync("ReceiveLatencyMetrics",
                new LatencyMetricsDto(sw.ElapsedMilliseconds, DateTime.UtcNow), ct);
        }
    }

    private async Task ProcessMessageAsync(string json)
    {
        try
        {
            var root = JObject.Parse(json);

            // Skip pong / subscription confirmation
            if (root["op"] != null) return;

            var topic = root["topic"]?.Value<string>() ?? "";
            if (!topic.StartsWith("kline.")) return;

            var symbol = topic.Split('.')[2];
            var klineArray = root["data"] as JArray;
            if (klineArray is null || klineArray.Count == 0) return;

            foreach (var klineToken in klineArray)
            {
                var candle = ParseCandle(klineToken);

                // Only process confirmed (closed) candles to avoid re-computing on every tick
                // "confirm" = true means the candle has closed in Bybit V5 kline stream
                var confirmed = klineToken["confirm"]?.Value<bool>() ?? false;
                if (!confirmed) continue;

                UpdateCandleWindow(symbol, candle);

                var atr = CalculateAtr(symbol);
                var currentPrice = candle.Close;

                // Recommended stop-loss = ATR * multiplier / price (as percentage)
                const double AtrMultiplier = 2.0;
                var stopLossPct = atr > 0 ? (decimal)AtrMultiplier * atr / currentPrice * 100m : 0m;
                var stopLossPrice = currentPrice - (decimal)AtrMultiplier * atr;

                // Broadcast price update
                await _hub.Clients.Group(symbol).SendAsync("ReceivePriceUpdate",
                    new PriceUpdateDto(symbol, currentPrice, DateTime.UtcNow));

                // Broadcast volatility data
                await _hub.Clients.Group(symbol).SendAsync("ReceiveVolatilityData",
                    new VolatilityDataDto(symbol, atr, stopLossPct, stopLossPrice, DateTime.UtcNow));
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to process WebSocket message: {Json}", json[..Math.Min(200, json.Length)]);
        }
    }

    // ─── ATR Calculation ──────────────────────────────────────────────────────

    private void UpdateCandleWindow(string symbol, KlineCandle candle)
    {
        if (!_candleWindows.TryGetValue(symbol, out var queue))
        {
            queue = new Queue<KlineCandle>(AtrPeriod + 1);
            _candleWindows[symbol] = queue;
        }

        queue.Enqueue(candle);

        // Keep window size to AtrPeriod + 1 (we need prev close for True Range)
        while (queue.Count > AtrPeriod + 1)
            queue.Dequeue();
    }

    /// <summary>
    /// Wilder's Smoothed ATR (classic 14-period).
    /// True Range = max(High - Low, |High - PrevClose|, |Low - PrevClose|)
    /// ATR = SMA of TR for first period, then Wilder smoothing thereafter.
    /// </summary>
    private decimal CalculateAtr(string symbol)
    {
        if (!_candleWindows.TryGetValue(symbol, out var queue)) return 0m;

        var candles = queue.ToArray();
        if (candles.Length < 2) return 0m;

        var trValues = new List<decimal>();

        for (int i = 1; i < candles.Length; i++)
        {
            var high = candles[i].High;
            var low = candles[i].Low;
            var prevClose = candles[i - 1].Close;

            var tr = Math.Max(high - low,
                     Math.Max(Math.Abs(high - prevClose),
                              Math.Abs(low - prevClose)));

            trValues.Add(tr);
        }

        if (trValues.Count == 0) return 0m;

        // Simple average for initial ATR
        if (trValues.Count < AtrPeriod)
            return trValues.Average();

        // Wilder smoothing: ATR(n) = (ATR(n-1) * (period-1) + TR(n)) / period
        var atr = trValues.Take(AtrPeriod).Average();
        for (int i = AtrPeriod; i < trValues.Count; i++)
            atr = (atr * (AtrPeriod - 1) + trValues[i]) / AtrPeriod;

        return Math.Round(atr, 8);
    }

    private static KlineCandle ParseCandle(JToken k) => new(
        Open: decimal.Parse(k["open"]?.Value<string>() ?? "0"),
        High: decimal.Parse(k["high"]?.Value<string>() ?? "0"),
        Low: decimal.Parse(k["low"]?.Value<string>() ?? "0"),
        Close: decimal.Parse(k["close"]?.Value<string>() ?? "0"),
        Volume: decimal.Parse(k["volume"]?.Value<string>() ?? "0"),
        Timestamp: DateTimeOffset.FromUnixTimeMilliseconds(k["start"]?.Value<long>() ?? 0).UtcDateTime);

    private record KlineCandle(
        decimal Open, decimal High, decimal Low, decimal Close,
        decimal Volume, DateTime Timestamp);
}
