using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using StarsAndSteel.Core.Entities;

namespace StarsAndSteel.Data;

/// <summary>
/// EF Core context for Stars &amp; Steel.
/// <para/>
/// Inherits <see cref="IdentityDbContext{TUser, TRole, TKey}"/> with Guid keys (not the default
/// non-generic IdentityDbContext, which uses string PKs). The matching DI registration in
/// <c>Program.cs</c> must use <c>AddIdentity&lt;User, IdentityRole&lt;Guid&gt;&gt;()</c> for these
/// type parameters to flow through correctly. See <c>docs/03-DATABASE-SCHEMA.md</c>.
/// <para/>
/// Phase 1B intentionally exposes only the Identity tables. Game-domain <c>DbSet&lt;T&gt;</c>s and
/// their <see cref="IEntityTypeConfiguration{TEntity}"/> mappings land in Phase 1C alongside
/// Migration 2 (<c>InitialGameWorld</c>) so that Migration 1 (<c>InitialIdentity</c>) generates a
/// clean Identity-only diff.
/// </summary>
public class StarsAndSteelDbContext : IdentityDbContext<User, IdentityRole<Guid>, Guid>
{
    public StarsAndSteelDbContext(DbContextOptions<StarsAndSteelDbContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        // Required by IdentityDbContext to register the Identity tables.
        base.OnModelCreating(builder);

        // Migration 1 (InitialIdentity) intentionally produces only the AspNet* tables.
        // The User entity has a Players navigation that pulls the entire game-domain
        // entity graph into the model. We mask that here so EF builds a clean Identity-
        // only model. Phase 1C will remove these ignores and add real configurations
        // alongside Migration 2 (InitialGameWorld).
        builder.Ignore<Player>();
        builder.Ignore<GameWorld>();
        builder.Ignore<Province>();
        builder.Ignore<ProvinceAdjacency>();
        builder.Ignore<Unit>();
        builder.Ignore<UnitOrder>();
        builder.Ignore<Building>();
        builder.Ignore<DiplomaticRelation>();
        builder.Ignore<ResearchProgress>();
        builder.Ignore<NewsItem>();
        builder.Ignore<ChatMessage>();
        builder.Ignore<AiMemory>();

        // Phase 1C will add: builder.ApplyConfigurationsFromAssembly(typeof(StarsAndSteelDbContext).Assembly);
        // once we have IEntityTypeConfiguration<T> classes in the Configurations folder.
    }
}
