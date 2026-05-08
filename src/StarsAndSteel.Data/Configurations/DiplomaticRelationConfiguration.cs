using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StarsAndSteel.Core.Entities;

namespace StarsAndSteel.Data.Configurations;

internal sealed class DiplomaticRelationConfiguration : IEntityTypeConfiguration<DiplomaticRelation>
{
    public void Configure(EntityTypeBuilder<DiplomaticRelation> b)
    {
        b.HasKey(x => x.Id);

        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired();

        b.HasOne(x => x.GameWorld)
            .WithMany()
            .HasForeignKey(x => x.GameWorldId)
            .OnDelete(DeleteBehavior.Cascade);

        // Two FKs to the same Player table: both NoAction to avoid cascade-path conflicts.
        // Diplomatic relations are wiped explicitly via the GameWorld cascade.
        b.HasOne(x => x.FromPlayer)
            .WithMany()
            .HasForeignKey(x => x.FromPlayerId)
            .OnDelete(DeleteBehavior.NoAction);

        b.HasOne(x => x.ToPlayer)
            .WithMany()
            .HasForeignKey(x => x.ToPlayerId)
            .OnDelete(DeleteBehavior.NoAction);

        b.HasIndex(x => new { x.GameWorldId, x.FromPlayerId });
    }
}
