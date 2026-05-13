using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StarsAndSteel.Core.Entities;

namespace StarsAndSteel.Data.Configurations;

internal sealed class GeneralConfiguration : IEntityTypeConfiguration<General>
{
    public void Configure(EntityTypeBuilder<General> b)
    {
        b.HasKey(x => x.Id);

        b.Property(x => x.Name).HasMaxLength(80).IsRequired();

        // Cascade from world: deleting a world removes its generals.
        b.HasOne(x => x.GameWorld)
            .WithMany()
            .HasForeignKey(x => x.GameWorldId)
            .OnDelete(DeleteBehavior.Cascade);

        // NoAction on player + province: those FKs already cascade through GameWorld and
        // SQL Server forbids multiple cascade paths to the same destination.
        b.HasOne(x => x.OwnerPlayer)
            .WithMany()
            .HasForeignKey(x => x.OwnerPlayerId)
            .OnDelete(DeleteBehavior.NoAction);

        // NoAction on AssignedProvince: SQL Server forbids multiple cascade paths
        // (Province cascades from GameWorld already). When a Province row is deleted
        // out of band, callers must null this column themselves; in practice the
        // only Province deletions happen via world deletion, which cascades
        // Generals via the GameWorldId FK.
        b.HasOne(x => x.AssignedProvince)
            .WithMany()
            .HasForeignKey(x => x.AssignedProvinceId)
            .OnDelete(DeleteBehavior.NoAction);

        // Hot path for CombatStep: "is there a general defending this province?".
        b.HasIndex(x => new { x.GameWorldId, x.AssignedProvinceId });
        // Enforces "one general per player" via service-level check (cheap + readable).
        b.HasIndex(x => new { x.GameWorldId, x.OwnerPlayerId });
    }
}
