using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StarsAndSteel.Core.Entities;

namespace StarsAndSteel.Data.Configurations;

internal sealed class ResearchProgressConfiguration : IEntityTypeConfiguration<ResearchProgress>
{
    public void Configure(EntityTypeBuilder<ResearchProgress> b)
    {
        b.HasKey(x => x.Id);

        b.Property(x => x.TechId).IsRequired().HasMaxLength(64);

        b.HasOne(x => x.Player)
            .WithMany()
            .HasForeignKey(x => x.PlayerId)
            .OnDelete(DeleteBehavior.Cascade);

        // One row per (player, tech).
        b.HasIndex(x => new { x.PlayerId, x.TechId }).IsUnique();
    }
}
