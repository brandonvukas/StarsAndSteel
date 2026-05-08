using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StarsAndSteel.Core.Entities;

namespace StarsAndSteel.Data.Configurations;

/// <summary>
/// Undirected adjacency edge stored once per pair. The invariant
/// <c>ProvinceAId &lt; ProvinceBId</c> is a code-side responsibility (seeders and the
/// adjacency-helper) — it is NOT enforced at the schema level because SQL Server can't compare
/// uniqueidentifier columns with a CHECK in a way EF can model cleanly. All adjacency lookups
/// must go through a helper that does <c>WHERE A = @id OR B = @id</c>.
/// </summary>
internal sealed class ProvinceAdjacencyConfiguration : IEntityTypeConfiguration<ProvinceAdjacency>
{
    public void Configure(EntityTypeBuilder<ProvinceAdjacency> b)
    {
        // Composite PK enforces "one row per pair".
        b.HasKey(x => new { x.ProvinceAId, x.ProvinceBId });

        b.Property(x => x.TerrainCost).HasDefaultValue(1.0f);

        // Restrict: with 2 FKs to Province, SQL Server can't allow cascade on both.
        // Adjacency rows are cleaned up explicitly when a province is removed.
        b.HasOne(x => x.ProvinceA)
            .WithMany()
            .HasForeignKey(x => x.ProvinceAId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasOne(x => x.ProvinceB)
            .WithMany()
            .HasForeignKey(x => x.ProvinceBId)
            .OnDelete(DeleteBehavior.Restrict);

        // Reverse-lookup index: "what touches province X via the B side?".
        // The composite PK already provides a leading index on (A, B), so lookups by A are fast.
        b.HasIndex(x => x.ProvinceBId);
    }
}
