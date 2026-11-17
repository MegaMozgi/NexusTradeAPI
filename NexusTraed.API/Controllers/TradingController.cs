using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NexusTraed.API.DTOs;
using NexusTraed.API.Services;

namespace NexusTraed.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class TradingController : ControllerBase
{
    private readonly ITradeSessionService _sessionService;
    private readonly IBybitHttpClientService _bybit;

    public TradingController(ITradeSessionService sessionService, IBybitHttpClientService bybit)
    {
        _sessionService = sessionService;
        _bybit = bybit;
    }

    /// <summary>Starts a new adaptive stop-loss trading session.</summary>
    [HttpPost("sessions")]
    [ProducesResponseType<TradeSessionDto>(StatusCodes.Status201Created)]
    public async Task<IActionResult> StartSession([FromBody] StartSessionDto dto)
    {
        var userId = GetUserId();
        var session = await _sessionService.StartSessionAsync(userId, dto);
        return CreatedAtAction(nameof(GetActiveSession),
            new { symbol = dto.Symbol }, session);
    }

    /// <summary>Gets the active session for a given symbol.</summary>
    [HttpGet("sessions/{symbol}")]
    [ProducesResponseType<TradeSessionDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetActiveSession(string symbol)
    {
        var userId = GetUserId();
        var session = await _sessionService.GetActiveSessionAsync(userId, symbol);
        return session is null ? NotFound() : Ok(session);
    }

    /// <summary>Stops / closes an active session.</summary>
    [HttpDelete("sessions/{sessionId:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> StopSession(int sessionId)
    {
        var userId = GetUserId();
        await _sessionService.StopSessionAsync(userId, sessionId);
        return NoContent();
    }

    /// <summary>Places a live market order on Bybit Testnet.</summary>
    [HttpPost("orders/market")]
    public async Task<IActionResult> PlaceMarketOrder(
        [FromQuery] string symbol,
        [FromQuery] string side,
        [FromQuery] decimal qty)
    {
        var userId = GetUserId();
        var success = await _bybit.PlaceMarketOrderAsync(userId, symbol, side, qty);
        return success ? Ok(new { message = "Order placed." }) : BadRequest(new { message = "Order failed." });
    }

    private int GetUserId() =>
        int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new UnauthorizedAccessException("User ID claim missing."));
}
