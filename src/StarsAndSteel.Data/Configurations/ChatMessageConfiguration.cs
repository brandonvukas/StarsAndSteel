using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StarsAndSteel.Core.Entities;

namespace StarsAndSteel.Data.Configurations;

internal sealed class ChatMessageConfiguration : IEntityTypeConfiguration<ChatMessage>
{
    public void Configure(EntityTypeBuilder<ChatMessage> b)
    {
        b.HasKey(x => x.Id);

        b.Property(x => x.Body).IsRequired().HasMaxLength(500);

        // Phase 2K: enum stored as string per project convention.
        b.Property(x => x.Scope).HasConversion<string>().HasMaxLength(20).IsRequired();

        b.HasOne(x => x.GameWorld)
            .WithMany()
            .HasForeignKey(x => x.GameWorldId)
            .OnDelete(DeleteBehavior.Cascade);

        // Two FKs to Player: both NoAction (multi-FK cycle avoidance).
        b.HasOne(x => x.FromPlayer)
            .WithMany()
            .HasForeignKey(x => x.FromPlayerId)
            .OnDelete(DeleteBehavior.NoAction);

        b.HasOne(x => x.ToPlayer)
            .WithMany()
            .HasForeignKey(x => x.ToPlayerId)
            .OnDelete(DeleteBehavior.NoAction);

        // Chat windows page by recency per world.
        b.HasIndex(x => new { x.GameWorldId, x.SentAtUtc });
    }
}
