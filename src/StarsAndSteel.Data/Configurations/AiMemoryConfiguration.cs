using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StarsAndSteel.Core.Entities;

namespace StarsAndSteel.Data.Configurations;

/// <summary>
/// 1:1 with <see cref="Player"/>. PK is also the FK; cascade on player deletion so AI memory
/// disappears with the seat.
/// </summary>
internal sealed class AiMemoryConfiguration : IEntityTypeConfiguration<AiMemory>
{
    public void Configure(EntityTypeBuilder<AiMemory> b)
    {
        b.HasKey(x => x.PlayerId);

        // nvarchar(max) for the JSON blob — schema-flexible AI internals.
        b.Property(x => x.MemoryJson).IsRequired().HasColumnType("nvarchar(max)");

        b.HasOne(x => x.Player)
            .WithOne(p => p.AiMemory!)
            .HasForeignKey<AiMemory>(x => x.PlayerId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
