namespace NexusTraed.API.DTOs;

// ─── Auth ────────────────────────────────────────────────────────────────────

public record RegisterDto(string Email, string Password);

public record LoginDto(string Email, string Password);

public record AuthResponseDto(string Token, string Email, int UserId);

// ─── API Credentials ─────────────────────────────────────────────────────────

public record SaveCredentialsDto(string ApiKey, string ApiSecret);

// ─── Trade Session ────────────────────────────────────────────────────────────

public record StartSessionDto(string Symbol, decimal EntryPrice, decimal InitialStopLossPrice);

public record TradeSessionDto(
    int Id,
    string Symbol,
    decimal EntryPrice,
    decimal InitialStopLossPrice,
    decimal CurrentAdaptiveStopLossPrice,
    bool IsActive,
    DateTime StartedAt);

// ─── Bybit Wallet ─────────────────────────────────────────────────────────────

public record WalletBalanceDto(List<CoinBalanceDto> Coins, decimal TotalEquityUsdt);

public record CoinBalanceDto(
    string Coin,
    decimal Free,
    decimal Locked,
    decimal EstimatedValueUsdt);

// ─── SignalR Broadcast ────────────────────────────────────────────────────────

public record PriceUpdateDto(string Symbol, decimal Price, DateTime Timestamp);

public record VolatilityDataDto(
    string Symbol,
    decimal Atr,
    decimal RecommendedStopLossPct,
    decimal RecommendedStopLossPrice,
    DateTime Timestamp);

public record LatencyMetricsDto(long PingMs, DateTime Timestamp);

// ─── Error ───────────────────────────────────────────────────────────────────

public record ErrorResponseDto(string Message, string? Detail = null, int StatusCode = 500);
