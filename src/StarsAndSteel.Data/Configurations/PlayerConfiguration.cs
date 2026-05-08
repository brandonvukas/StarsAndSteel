using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StarsAndSteel.Core.Entities;

namespace StarsAndSteel.Data.Configurations;

internal sealed class PlayerConfiguration : IEntityTypeConfiguration<Player>
{
    public void Configure(EntityTypeBuilder<Player> b)
    {
        b.HasKey(x => x.Id);

        b.Property(x => x.NationName).IsRequired().HasMaxLength(80);
        b.Property(x => x.FlagPrimaryHex).IsRequired().HasMaxLength(7);
        b.Property(x => x.FlagSecondaryHex).IsRequired().HasMaxLength(7);

        b.Property(x => x.AiPersonality).HasConversion<string>().HasMaxLength(20);

        // User ↔ Player: a User has many Players (one per game). UserId is null for AI seats.
        // SetNull on user deletion: keep the historical seat but disconnect it from the (gone) account.
        b.HasOne(x => x.User)
            .WithMany(u => u.Players)
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.SetNull);

        // Provinces and Units owned by this Player. Both NoAction: cascading would create a
        // second path from GameWorld → Provinces (already cascading via Provinces.GameWorldId),
        // and SQL Server forbids multiple cascade paths to the same table. World-end cleanup
        // is responsible for clearing OwnerPlayerId on provinces and removing units explicitly
        // before deleting the GameWorld row.
        b.HasMany(x => x.OwnedProvinces)
            .WithOne(p => p.OwnerPlayer)
            .HasForeignKey(p => p.OwnerPlayerId)
            .OnDelete(DeleteBehavior.NoAction);

        // 1:1 with AiMemory (configured on AiMemory side).

        // Useful index for "what's my roster?" - scoped to the world.
        b.HasIndex(x => new { x.GameWorldId, x.IsAi });
    }
}
