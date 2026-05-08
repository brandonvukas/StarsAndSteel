using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StarsAndSteel.Core.Entities;
using StarsAndSteel.Core.Enums;

namespace StarsAndSteel.Data.Configurations;

internal sealed class GameWorldConfiguration : IEntityTypeConfiguration<GameWorld>
{
    public void Configure(EntityTypeBuilder<GameWorld> b)
    {
        b.HasKey(x => x.Id);

        b.Property(x => x.Name).IsRequired().HasMaxLength(100);

        // Stored as string for forensic readability ("Active" vs 1) and refactor safety.
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(16).IsRequired();

        b.Property(x => x.TickIntervalSeconds).HasDefaultValue(60);

        // SQL Server rowversion. Used by the tick processor as an optimistic-concurrency token
        // so a tick that started against an older snapshot fails fast rather than overwriting.
        b.Property(x => x.RowVersion).IsRowVersion().IsConcurrencyToken();

        b.HasMany(x => x.Players)
            .WithOne(p => p.GameWorld)
            .HasForeignKey(p => p.GameWorldId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasMany(x => x.Provinces)
            .WithOne(p => p.GameWorld)
            .HasForeignKey(p => p.GameWorldId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasMany(x => x.NewsItems)
            .WithOne(n => n.GameWorld)
            .HasForeignKey(n => n.GameWorldId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
