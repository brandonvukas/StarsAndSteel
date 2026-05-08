using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StarsAndSteel.Core.Entities;

namespace StarsAndSteel.Data.Configurations;

internal sealed class UnitOrderConfiguration : IEntityTypeConfiguration<UnitOrder>
{
    public void Configure(EntityTypeBuilder<UnitOrder> b)
    {
        b.HasKey(x => x.Id);

        b.Property(x => x.OrderType).HasConversion<string>().HasMaxLength(16).IsRequired();
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(16).IsRequired();

        b.HasOne(x => x.Unit)
            .WithMany(u => u.Orders)
            .HasForeignKey(x => x.UnitId)
            .OnDelete(DeleteBehavior.Cascade);

        // Target province: NoAction. The order is meaningless if the target province vanishes,
        // but cascading would create another path through Province; the tick processor ignores
        // orders whose target no longer exists.
        b.HasOne(x => x.TargetProvince)
            .WithMany()
            .HasForeignKey(x => x.TargetProvinceId)
            .OnDelete(DeleteBehavior.NoAction);

        // The hot path for the tick processor: "what orders are pending and due this tick?"
        b.HasIndex(x => new { x.Status, x.IssuedAtTick });
    }
}
