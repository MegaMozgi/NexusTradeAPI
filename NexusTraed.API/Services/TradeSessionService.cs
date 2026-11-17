using Microsoft.EntityFrameworkCore;
using NexusTraed.API.Data;
using NexusTraed.API.DTOs;
using NexusTraed.API.Models;

namespace NexusTraed.API.Services;

public interface ITradeSessionService
{
    Task<TradeSessionDto> StartSessionAsync(int userId, StartSessionDto dto);
    Task<TradeSessionDto?> GetActiveSessionAsync(int userId, string symbol);
    Task StopSessionAsync(int userId, int sessionId);
    Task UpdateAdaptiveStopLossAsync(int sessionId, decimal newStopLossPrice);
}

public class TradeSessionService : ITradeSessionService
{
    private readonly AppDbContext _db;

    public TradeSessionService(AppDbContext db) => _db = db;

    public async Task<TradeSessionDto> StartSessionAsync(int userId, StartSessionDto dto)
    {
        // Deactivate existing sessions for the same symbol
        var existing = await _db.TradeSessions
            .Where(t => t.UserId == userId && t.Symbol == dto.Symbol && t.IsActive)
            .ToListAsync();

        existing.ForEach(t => { t.IsActive = false; t.EndedAt = DateTime.UtcNow; });

        var session = new TradeSession
        {
            UserId = userId,
            Symbol = dto.Symbol.ToUpperInvariant(),
            EntryPrice = dto.EntryPrice,
            InitialStopLossPrice = dto.InitialStopLossPrice,
            CurrentAdaptiveStopLossPrice = dto.InitialStopLossPrice,
            IsActive = true,
            StartedAt = DateTime.UtcNow
        };

        _db.TradeSessions.Add(session);
        await _db.SaveChangesAsync();

        return MapToDto(session);
    }

    public async Task<TradeSessionDto?> GetActiveSessionAsync(int userId, string symbol)
    {
        var session = await _db.TradeSessions
            .FirstOrDefaultAsync(t => t.UserId == userId
                && t.Symbol == symbol.ToUpperInvariant()
                && t.IsActive);

        return session is null ? null : MapToDto(session);
    }

    public async Task StopSessionAsync(int userId, int sessionId)
    {
        var session = await _db.TradeSessions
            .FirstOrDefaultAsync(t => t.Id == sessionId && t.UserId == userId)
            ?? throw new KeyNotFoundException("Trade session not found.");

        session.IsActive = false;
        session.EndedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
    }

    public async Task UpdateAdaptiveStopLossAsync(int sessionId, decimal newStopLossPrice)
    {
        var session = await _db.TradeSessions.FindAsync(sessionId);
        if (session is null || !session.IsActive) return;

        session.CurrentAdaptiveStopLossPrice = newStopLossPrice;
        await _db.SaveChangesAsync();
    }

    private static TradeSessionDto MapToDto(TradeSession s) => new(
        s.Id, s.Symbol, s.EntryPrice,
        s.InitialStopLossPrice, s.CurrentAdaptiveStopLossPrice,
        s.IsActive, s.StartedAt);
}
