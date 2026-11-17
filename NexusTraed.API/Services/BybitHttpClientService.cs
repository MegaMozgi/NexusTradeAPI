using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NexusTraed.API.Data;
using NexusTraed.API.DTOs;

namespace NexusTraed.API.Services;

public interface IBybitHttpClientService
{
    Task<bool> PlaceMarketOrderAsync(int userId, string symbol, string side, decimal qty);
    Task<bool> AmendStopLossAsync(int userId, string symbol, string orderId, decimal newStopLoss);
    Task<WalletBalanceDto> GetWalletBalanceAsync(int userId);
}

public class BybitHttpClientService : IBybitHttpClientService
{
    private const string BaseUrl = "https://api-testnet.bybit.com";

    private readonly HttpClient _http;
    private readonly AppDbContext _db;
    private readonly IEncryptionService _encryption;
    private readonly ILogger<BybitHttpClientService> _logger;

    public BybitHttpClientService(
        HttpClient http,
        AppDbContext db,
        IEncryptionService encryption,
        ILogger<BybitHttpClientService> logger)
    {
        _http = http;
        _db = db;
        _encryption = encryption;
        _logger = logger;
        _http.BaseAddress = new Uri(BaseUrl);
        _http.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));
    }

    // ─── Public Methods ───────────────────────────────────────────────────────

    public async Task<bool> PlaceMarketOrderAsync(int userId, string symbol, string side, decimal qty)
    {
        var (apiKey, apiSecret) = await GetDecryptedKeysAsync(userId);

        var payload = new Dictionary<string, object>
        {
            ["category"] = "linear",
            ["symbol"] = symbol,
            ["side"] = side,
            ["orderType"] = "Market",
            ["qty"] = qty.ToString("F3")
        };

        var response = await SendSignedPostAsync("/v5/order/create", payload, apiKey, apiSecret);
        return IsSuccess(response);
    }

    public async Task<bool> AmendStopLossAsync(int userId, string symbol, string orderId, decimal newStopLoss)
    {
        var (apiKey, apiSecret) = await GetDecryptedKeysAsync(userId);

        var payload = new Dictionary<string, object>
        {
            ["category"] = "linear",
            ["symbol"] = symbol,
            ["orderId"] = orderId,
            ["stopLoss"] = newStopLoss.ToString("F2")
        };

        var response = await SendSignedPostAsync("/v5/order/amend", payload, apiKey, apiSecret);
        return IsSuccess(response);
    }

    public async Task<WalletBalanceDto> GetWalletBalanceAsync(int userId)
    {
        var (apiKey, apiSecret) = await GetDecryptedKeysAsync(userId);

        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
        var recvWindow = "5000";
        var queryString = $"accountType=UNIFIED";
        var signPayload = $"{timestamp}{apiKey}{recvWindow}{queryString}";
        var signature = Sign(signPayload, apiSecret);

        var request = new HttpRequestMessage(HttpMethod.Get,
            $"/v5/account/wallet-balance?{queryString}");

        request.Headers.Add("X-BAPI-API-KEY", apiKey);
        request.Headers.Add("X-BAPI-TIMESTAMP", timestamp);
        request.Headers.Add("X-BAPI-SIGN", signature);
        request.Headers.Add("X-BAPI-RECV-WINDOW", recvWindow);

        var httpResponse = await _http.SendAsync(request);
        var json = await httpResponse.Content.ReadAsStringAsync();

        return ParseWalletBalance(json);
    }

    // ─── Private Helpers ──────────────────────────────────────────────────────

    private async Task<(string ApiKey, string ApiSecret)> GetDecryptedKeysAsync(int userId)
    {
        var creds = await _db.ApiCredentials.FirstOrDefaultAsync(c => c.UserId == userId)
            ?? throw new KeyNotFoundException($"No API credentials found for user {userId}.");

        return (
            _encryption.Decrypt(creds.EncryptedApiKey),
            _encryption.Decrypt(creds.EncryptedApiSecret)
        );
    }

    private async Task<JObject?> SendSignedPostAsync(
        string endpoint,
        Dictionary<string, object> payload,
        string apiKey,
        string apiSecret)
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
        var recvWindow = "5000";
        var body = JsonConvert.SerializeObject(payload);
        var signPayload = $"{timestamp}{apiKey}{recvWindow}{body}";
        var signature = Sign(signPayload, apiSecret);

        var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };

        request.Headers.Add("X-BAPI-API-KEY", apiKey);
        request.Headers.Add("X-BAPI-TIMESTAMP", timestamp);
        request.Headers.Add("X-BAPI-SIGN", signature);
        request.Headers.Add("X-BAPI-RECV-WINDOW", recvWindow);

        var response = await _http.SendAsync(request);
        var json = await response.Content.ReadAsStringAsync();

        _logger.LogDebug("Bybit {Endpoint} response: {Json}", endpoint, json);

        return JsonConvert.DeserializeObject<JObject>(json);
    }

    private static string Sign(string payload, string secret)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
    }

    private static bool IsSuccess(JObject? response) =>
        response?["retCode"]?.Value<int>() == 0;

    private static WalletBalanceDto ParseWalletBalance(string json)
    {
        var root = JObject.Parse(json);

        if (root["retCode"]?.Value<int>() != 0)
            throw new Exception($"Bybit API error: {root["retMsg"]}");

        var coins = new List<CoinBalanceDto>();

        var accounts = root["result"]?["list"] as JArray ?? [];

        foreach (var account in accounts)
        {
            var coinList = account["coin"] as JArray ?? [];
            foreach (var c in coinList)
            {
                var coin = c["coin"]?.Value<string>() ?? "UNKNOWN";
                var free = decimal.TryParse(c["walletBalance"]?.Value<string>(), out var f) ? f : 0m;
                var locked = decimal.TryParse(c["locked"]?.Value<string>(), out var l) ? l : 0m;
                var usdtValue = decimal.TryParse(c["usdValue"]?.Value<string>(), out var u) ? u : 0m;

                if (free + locked == 0) continue;

                coins.Add(new CoinBalanceDto(coin, free, locked, usdtValue));
            }
        }

        var total = coins.Sum(c => c.EstimatedValueUsdt);
        return new WalletBalanceDto(coins, total);
    }
}
