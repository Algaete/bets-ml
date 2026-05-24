using CornersPrediction.Domain.MatchHistory;
using Microsoft.EntityFrameworkCore;

namespace CornersPrediction.Infrastructure.Persistence;

public sealed class CornersPredictionDbContext : DbContext
{
    public CornersPredictionDbContext(DbContextOptions<CornersPredictionDbContext> options)
        : base(options)
    {
    }

    public DbSet<MatchHistoryItem> MatchHistoryItems => Set<MatchHistoryItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<MatchHistoryItem>(entity =>
        {
            entity.ToTable("match_history");
            entity.HasKey(item => item.Id);

            entity.Property(item => item.League).HasMaxLength(120).IsRequired();
            entity.Property(item => item.Season).HasMaxLength(40).IsRequired();
            entity.Property(item => item.HomeTeam).HasMaxLength(120).IsRequired();
            entity.Property(item => item.AwayTeam).HasMaxLength(120).IsRequired();
            entity.Property(item => item.HomeFormation).HasMaxLength(30);
            entity.Property(item => item.AwayFormation).HasMaxLength(30);
            entity.Property(item => item.CreatedAtUtc).IsRequired();

            entity.Ignore(item => item.TotalCorners);
            entity.HasIndex(item => new { item.League, item.Season, item.MatchDate });
            entity.HasIndex(item => item.HomeTeam);
            entity.HasIndex(item => item.AwayTeam);
        });
    }
}
