using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NexusTraed.API.Data;
using NexusTraed.API.DTOs;
using NexusTraed.API.Models;
using NexusTraed.API.Services;

namespace NexusTraed.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class CredentialsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IEncryptionService _encryption;

    public CredentialsController(AppDbContext db, IEncryptionService encryption)
    {
        _db = db;
        _encryption = encryption;
    }

    [HttpPost]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> Save([FromBody] SaveCredentialsDto dto)
    {
        var userId = GetUserId();

        var existing = await _db.ApiCredentials.FirstOrDefaultAsync(c => c.UserId == userId);

        if (existing is null)
        {
            _db.ApiCredentials.Add(new ApiCredentials
            {
                UserId = userId,
                EncryptedApiKey = _encryption.Encrypt(dto.ApiKey),
                EncryptedApiSecret = _encryption.Encrypt(dto.ApiSecret),
                ExchangeName = "BybitTestnet"
            });
        }
        else
        {
            existing.EncryptedApiKey = _encryption.Encrypt(dto.ApiKey);
            existing.EncryptedApiSecret = _encryption.Encrypt(dto.ApiSecret);
            existing.UpdatedAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync();
        return Ok(new { message = "API credentials saved successfully." });
    }

    [HttpGet("exists")]
    public async Task<IActionResult> Exists()
    {
        var userId = GetUserId();
        var exists = await _db.ApiCredentials.AnyAsync(c => c.UserId == userId);
        return Ok(new { hasCredentials = exists });
    }

    [HttpDelete]
    public async Task<IActionResult> Delete()
    {
        var userId = GetUserId();
        var creds = await _db.ApiCredentials.FirstOrDefaultAsync(c => c.UserId == userId);
        if (creds is null) return NotFound();

        _db.ApiCredentials.Remove(creds);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    private int GetUserId() =>
        int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new UnauthorizedAccessException("User ID claim missing."));
}
