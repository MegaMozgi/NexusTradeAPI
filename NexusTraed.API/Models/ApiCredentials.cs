using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace NexusTraed.API.Models;

public class ApiCredentials
{
    public int Id { get; set; }

    public int UserId { get; set; }

    [Required]
    public string EncryptedApiKey { get; set; } = string.Empty;

    [Required]
    public string EncryptedApiSecret { get; set; } = string.Empty;

    [MaxLength(64)]
    public string ExchangeName { get; set; } = "BybitTestnet";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    [ForeignKey(nameof(UserId))]
    public User User { get; set; } = null!;
}
