namespace NexusTraed.API.Hubs;

using Microsoft.AspNetCore.SignalR;

/// <summary>
/// SignalR hub for real-time trading data push to React frontend.
/// Clients subscribe using their JWT token (passed as access_token query param).
/// </summary>
public class TradingHub : Hub
{
    private readonly ILogger<TradingHub> _logger;

    public TradingHub(ILogger<TradingHub> logger) => _logger = logger;

    public override async Task OnConnectedAsync()
    {
        var userId = Context.UserIdentifier;
        _logger.LogInformation("TradingHub: Client connected. UserId={UserId} ConnId={ConnId}",
            userId, Context.ConnectionId);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _logger.LogInformation("TradingHub: Client disconnected. ConnId={ConnId}", Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>Client calls this to subscribe to a symbol's live stream.</summary>
    public async Task SubscribeToSymbol(string symbol)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, symbol.ToUpperInvariant());
        _logger.LogInformation("Client {ConnId} subscribed to {Symbol}", Context.ConnectionId, symbol);
    }

    public async Task UnsubscribeFromSymbol(string symbol)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, symbol.ToUpperInvariant());
    }
}
