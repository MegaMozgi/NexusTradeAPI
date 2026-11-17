using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NexusTraed.API.DTOs;
using NexusTraed.API.Services;

namespace NexusTraed.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class PortfolioController : ControllerBase
{
    private readonly IBybitHttpClientService _bybit;

    public PortfolioController(IBybitHttpClientService bybit) => _bybit = bybit;

    /// <summary>
    /// Fetches the authenticated user's unified wallet balance from Bybit Testnet.
    /// Used by the React frontend to render a Donut Chart.
    /// </summary>
    [HttpGet("balance")]
    [ProducesResponseType<WalletBalanceDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetBalance()
    {
        var userId = GetUserId();
        var balance = await _bybit.GetWalletBalanceAsync(userId);
        return Ok(balance);
    }

    private int GetUserId() =>
        int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new UnauthorizedAccessException("User ID claim missing."));
}
