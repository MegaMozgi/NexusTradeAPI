using Microsoft.EntityFrameworkCore;
using NexusTraed.API.Models;

namespace NexusTraed.API.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<ApiCredentials> ApiCredentials => Set<ApiCredentials>();
    public DbSet<TradeSession> TradeSessions => Set<TradeSession>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // User
        modelBuilder.Entity<User>(e =>
        {
            e.HasIndex(u => u.Email).IsUnique();
            e.Property(u => u.Email).IsRequired().HasMaxLength(256);
        });

        // ApiCredentials — one-to-one with User
        modelBuilder.Entity<ApiCredentials>(e =>
        {
            e.HasOne(a => a.User)
             .WithOne(u => u.ApiCredentials)
             .HasForeignKey<ApiCredentials>(a => a.UserId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        // TradeSession — many-to-one with User
        modelBuilder.Entity<TradeSession>(e =>
        {
            e.HasOne(t => t.User)
             .WithMany(u => u.TradeSessions)
             .HasForeignKey(t => t.UserId)
             .OnDelete(DeleteBehavior.Cascade);

            e.HasIndex(t => new { t.UserId, t.IsActive });
        });
    }
}
