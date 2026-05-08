using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StarsAndSteel.Core.Entities;

namespace StarsAndSteel.Data.Configurations;

internal sealed class ProvinceConfiguration : IEntityTypeConfiguration<Province>
{
    public void Configure(EntityTypeBuilder<Province> b)
    {
        b.HasKey(x => x.Id);

        b.Property(x => x.Name).IsRequired().HasMaxLength(100);
        b.Property(x => x.Type).HasConversion<string>().HasMaxLength(20).IsRequired();

        // GameWorld → Province cascade is configured on GameWorld side.
        // OwnerPlayer → Province SetNull is configured on Player side.

        // Indexes from docs/03-DATABASE-SCHEMA.md "Indexes (the ones that matter)".
        b.HasIndex(x => x.GameWorldId);
        b.HasIndex(x => new { x.GameWorldId, x.OwnerPlayerId });

        // Province → Buildings cascade (deleting a province wipes its buildings).
        b.HasMany(x => x.Buildings)
            .WithOne(bld => bld.Province)
            .HasForeignKey(bld => bld.ProvinceId)
            .OnDelete(DeleteBehavior.Cascade);

        // Province → Units (UnitsStationed via LocationProvinceId).
        // Configured fully on Unit side because Unit has 4 FKs to Province and only ONE of them
        // is the inverse of UnitsStationed; specifying it here would make EF guess wrong.
        b.HasMany(x => x.UnitsStationed)
            .WithOne(u => u.LocationProvince!)
            .HasForeignKey(u => u.LocationProvinceId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
