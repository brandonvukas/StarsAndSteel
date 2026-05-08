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
/// Per-entity mapping (keys, indexes, FKs, string lengths, enum-as-string conversions) lives in
/// <c>StarsAndSteel.Data/Configurations/</c> as <see cref="IEntityTypeConfiguration{TEntity}"/>
/// classes. <see cref="OnModelCreating"/> applies them en masse so adding a new entity is a
/// single-file change, not a context edit.
/// </summary>
public class StarsAndSteelDbContext : IdentityDbContext<User, IdentityRole<Guid>, Guid>
{
    public StarsAndSteelDbContext(DbContextOptions<StarsAndSteelDbContext> options)
        : base(options)
    {
    }

    // Game-domain DbSets. The Identity DbSets (Users, Roles, etc.) come from the base class.
    public DbSet<GameWorld> GameWorlds => Set<GameWorld>();
    public DbSet<Player> Players => Set<Player>();
    public DbSet<Province> Provinces => Set<Province>();
    public DbSet<ProvinceAdjacency> ProvinceAdjacencies => Set<ProvinceAdjacency>();
    public DbSet<Unit> Units => Set<Unit>();
    public DbSet<UnitOrder> UnitOrders => Set<UnitOrder>();
    public DbSet<Building> Buildings => Set<Building>();
    public DbSet<DiplomaticRelation> DiplomaticRelations => Set<DiplomaticRelation>();
    public DbSet<ResearchProgress> ResearchProgress => Set<ResearchProgress>();
    public DbSet<NewsItem> NewsItems => Set<NewsItem>();
    public DbSet<ChatMessage> ChatMessages => Set<ChatMessage>();
    public DbSet<AiMemory> AiMemories => Set<AiMemory>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        // Identity tables first.
        base.OnModelCreating(builder);

        // Then everything in StarsAndSteel.Data/Configurations/ via reflection.
        builder.ApplyConfigurationsFromAssembly(typeof(StarsAndSteelDbContext).Assembly);
    }
}
