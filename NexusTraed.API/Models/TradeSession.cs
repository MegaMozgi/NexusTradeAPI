using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace NexusTraed.API.Models;

public class TradeSession
{
    public int Id { get; set; }

    public int UserId { get; set; }

    [Required, MaxLength(20)]
    public string Symbol { get; set; } = string.Empty;

    [Column(TypeName = "decimal(18,8)")]
    public decimal EntryPrice { get; set; }

    [Column(TypeName = "decimal(18,8)")]
    public decimal InitialStopLossPrice { get; set; }

    [Column(TypeName = "decimal(18,8)")]
    public decimal CurrentAdaptiveStopLossPrice { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime StartedAt { get; set; } = DateTime.UtcNow;

    public DateTime? EndedAt { get; set; }

    // Navigation
    [ForeignKey(nameof(UserId))]
    public User User { get; set; } = null!;
}
