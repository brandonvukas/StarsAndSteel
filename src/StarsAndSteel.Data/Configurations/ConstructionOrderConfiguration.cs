using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StarsAndSteel.Core.Entities;

namespace StarsAndSteel.Data.Configurations;

internal sealed class ConstructionOrderConfiguration : IEntityTypeConfiguration<ConstructionOrder>
{
    public void Configure(EntityTypeBuilder<ConstructionOrder> b)
    {
        b.HasKey(x => x.Id);

        b.Property(x => x.OrderType).HasConversion<string>().HasMaxLength(16).IsRequired();
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(16).IsRequired();
        b.Property(x => x.UnitType).HasConversion<string>().HasMaxLength(32);
        b.Property(x => x.BuildingType).HasConversion<string>().HasMaxLength(32);

        // Cascade from world: deleting a world deletes all its in-flight construction.
        b.HasOne(x => x.GameWorld)
            .WithMany()
            .HasForeignKey(x => x.GameWorldId)
            .OnDelete(DeleteBehavior.Cascade);

        // NoAction on player + province: those FKs already cascade through GameWorld, and SQL
        // Server forbids multiple cascade paths to the same table.
        b.HasOne(x => x.OwnerPlayer)
            .WithMany()
            .HasForeignKey(x => x.OwnerPlayerId)
            .OnDelete(DeleteBehavior.NoAction);

        b.HasOne(x => x.Province)
            .WithMany()
            .HasForeignKey(x => x.ProvinceId)
            .OnDelete(DeleteBehavior.NoAction);

        // The hot path for ConstructionStep: "what construction is pending and due this tick?"
        b.HasIndex(x => new { x.GameWorldId, x.Status, x.IssuedAtTick });
    }
}
