using System.ComponentModel.DataAnnotations;

namespace NexusTraed.API.Models;

public class User
{
    public int Id { get; set; }

    [Required, MaxLength(256)]
    public string Email { get; set; } = string.Empty;

    [Required]
    public string PasswordHash { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public ApiCredentials? ApiCredentials { get; set; }
    public ICollection<TradeSession> TradeSessions { get; set; } = [];
}
