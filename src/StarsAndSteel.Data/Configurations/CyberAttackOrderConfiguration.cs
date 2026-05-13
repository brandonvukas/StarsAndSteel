using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StarsAndSteel.Core.Entities;

namespace StarsAndSteel.Data.Configurations;

internal sealed class CyberAttackOrderConfiguration : IEntityTypeConfiguration<CyberAttackOrder>
{
    public void Configure(EntityTypeBuilder<CyberAttackOrder> b)
    {
        b.HasKey(x => x.Id);

        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(16).IsRequired();
        b.Property(x => x.EffectKind).HasConversion<string>().HasMaxLength(24);

        // Cascade from world: deleting a world deletes all its in-flight cyber orders.
        b.HasOne(x => x.GameWorld)
            .WithMany()
            .HasForeignKey(x => x.GameWorldId)
            .OnDelete(DeleteBehavior.Cascade);

        // NoAction on player + provinces — those FKs already cascade through GameWorld,
        // and SQL Server forbids multiple cascade paths to the same table.
        b.HasOne(x => x.AttackerPlayer)
            .WithMany()
            .HasForeignKey(x => x.AttackerPlayerId)
            .OnDelete(DeleteBehavior.NoAction);

        b.HasOne(x => x.LaunchProvince)
            .WithMany()
            .HasForeignKey(x => x.LaunchProvinceId)
            .OnDelete(DeleteBehavior.NoAction);

        b.HasOne(x => x.TargetProvince)
            .WithMany()
            .HasForeignKey(x => x.TargetProvinceId)
            .OnDelete(DeleteBehavior.NoAction);

        // Hot path for CyberAttackStep: "what cyber orders are pending and due this tick?"
        b.HasIndex(x => new { x.GameWorldId, x.Status, x.IssuedAtTick });
    }
}
