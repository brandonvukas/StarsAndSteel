using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StarsAndSteel.Core.Entities;

namespace StarsAndSteel.Data.Configurations;

internal sealed class NewsItemConfiguration : IEntityTypeConfiguration<NewsItem>
{
    public void Configure(EntityTypeBuilder<NewsItem> b)
    {
        b.HasKey(x => x.Id);

        b.Property(x => x.Headline).IsRequired().HasMaxLength(200);
        b.Property(x => x.Body).IsRequired().HasMaxLength(2000);

        b.Property(x => x.Severity).HasConversion<string>().HasMaxLength(12).IsRequired();
        b.Property(x => x.Category).HasConversion<string>().HasMaxLength(16).IsRequired();

        // GameWorld FK cascade configured on GameWorld side.

        b.HasOne(x => x.RelatedPlayer)
            .WithMany()
            .HasForeignKey(x => x.RelatedPlayerId)
            .OnDelete(DeleteBehavior.NoAction);

        // Hot path: cable-news ticker reads "newest first per world".
        b.HasIndex(x => new { x.GameWorldId, x.Tick }).IsDescending(false, true);
    }
}
