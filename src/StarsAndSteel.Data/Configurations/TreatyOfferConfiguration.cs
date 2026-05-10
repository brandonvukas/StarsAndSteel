using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StarsAndSteel.Core.Entities;

namespace StarsAndSteel.Data.Configurations;

internal sealed class TreatyOfferConfiguration : IEntityTypeConfiguration<TreatyOffer>
{
    public void Configure(EntityTypeBuilder<TreatyOffer> b)
    {
        b.HasKey(x => x.Id);

        b.Property(x => x.Kind).HasConversion<string>().HasMaxLength(20).IsRequired();
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired();

        b.HasOne(x => x.GameWorld)
            .WithMany()
            .HasForeignKey(x => x.GameWorldId)
            .OnDelete(DeleteBehavior.Cascade);

        // Two FKs to the same Player table: NoAction on both to avoid SQL Server multiple
        // cascade-paths error. Treaty offers are wiped via the GameWorld cascade on world delete.
        b.HasOne(x => x.SenderPlayer)
            .WithMany()
            .HasForeignKey(x => x.SenderPlayerId)
            .OnDelete(DeleteBehavior.NoAction);

        b.HasOne(x => x.ReceiverPlayer)
            .WithMany()
            .HasForeignKey(x => x.ReceiverPlayerId)
            .OnDelete(DeleteBehavior.NoAction);

        // Most common query: "what pending offers does this receiver have in this world?"
        b.HasIndex(x => new { x.GameWorldId, x.ReceiverPlayerId, x.Status });
        // Secondary: tick pipeline scans expired pending offers per world.
        b.HasIndex(x => new { x.GameWorldId, x.Status, x.ExpiresAtTick });
    }
}
