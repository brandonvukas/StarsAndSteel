using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StarsAndSteel.Core.Entities;

namespace StarsAndSteel.Data.Configurations;

internal sealed class UnitConfiguration : IEntityTypeConfiguration<Unit>
{
    public void Configure(EntityTypeBuilder<Unit> b)
    {
        b.HasKey(x => x.Id);

        b.Property(x => x.Type).HasConversion<string>().HasMaxLength(24).IsRequired();
        b.Property(x => x.Domain).HasConversion<string>().HasMaxLength(8).IsRequired();

        // GameWorld → Unit: NoAction. SQL Server already cascades GameWorld → Player → Unit
        // (via OwnerPlayer) and GameWorld → Province → Unit (via Location); a third cascade path
        // would create the dreaded "multiple cascade paths" error.
        b.HasOne(x => x.GameWorld)
            .WithMany()
            .HasForeignKey(x => x.GameWorldId)
            .OnDelete(DeleteBehavior.NoAction);

        // Owner: NoAction for the same reason; Player cleanup happens via the world cascade
        // and explicit ordering in the world-delete code path (post-MVP).
        b.HasOne(x => x.OwnerPlayer)
            .WithMany(p => p.OwnedUnits)
            .HasForeignKey(x => x.OwnerPlayerId)
            .OnDelete(DeleteBehavior.NoAction);

        // The 4 Province FKs. LocationProvince's inverse (UnitsStationed) is configured on
        // Province; the rest are unidirectional (no inverse navigation on Province) so we don't
        // confuse EF about which navigation maps to which FK.
        // All NoAction: 4 FKs to one table = guaranteed cascade-path conflict otherwise.
        b.HasOne(x => x.TransitFromProvince)
            .WithMany()
            .HasForeignKey(x => x.TransitFromProvinceId)
            .OnDelete(DeleteBehavior.NoAction);

        b.HasOne(x => x.TransitToProvince)
            .WithMany()
            .HasForeignKey(x => x.TransitToProvinceId)
            .OnDelete(DeleteBehavior.NoAction);

        b.HasOne(x => x.HomeBaseProvince)
            .WithMany()
            .HasForeignKey(x => x.HomeBaseProvinceId)
            .OnDelete(DeleteBehavior.NoAction);

        // Phase 2b: self-FK for carrier-air-wing → carrier parenting. NoAction because
        // the GameWorld cascade already covers cleanup; we handle "carrier sunk → wings
        // destroyed" explicitly in CombatStep so the in-memory model stays consistent.
        b.HasOne(x => x.ParentUnit)
            .WithMany()
            .HasForeignKey(x => x.ParentUnitId)
            .OnDelete(DeleteBehavior.NoAction);

        // Indexes from docs/03-DATABASE-SCHEMA.md.
        b.HasIndex(x => new { x.GameWorldId, x.OwnerPlayerId });
        b.HasIndex(x => x.LocationProvinceId);
        b.HasIndex(x => x.Domain); // "all enemy aircraft this tick"
        b.HasIndex(x => x.ParentUnitId); // for "all wings on this carrier"
    }
}
