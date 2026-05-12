using StarsAndSteel.Core.Enums;

namespace StarsAndSteel.Core.Entities;

/// <summary>
/// The atomic unit of territory. Owned by a <see cref="Player"/> (or null = neutral).
/// Resource production is read directly off this row each tick by <c>ResourceProductionStep</c>;
/// buildings additively modify those base values during the step.
/// </summary>
public class Province
{
    public Guid Id { get; set; }
    public Guid GameWorldId { get; set; }
    public GameWorld GameWorld { get; set; } = default!;

    public string Name { get; set; } = default!;
    public ProvinceType Type { get; set; }

    /// <summary>Null for neutral provinces.</summary>
    public Guid? OwnerPlayerId { get; set; }
    public Player? OwnerPlayer { get; set; }

    /// <summary>Required for naval-unit movement and for sea-crossing adjacency.</summary>
    public bool IsCoastal { get; set; }

    public float CenterX { get; set; }
    public float CenterY { get; set; }

    /// <summary>0-100. Drops on combat losses and occupation; recovers slowly toward 100.</summary>
    public int MoraleLevel { get; set; } = 100;

    /// <summary>
    /// Phase 3a: 0-100 permanent radiation level applied by nuclear strikes. Multiplies
    /// resource output by (1 - RadiationLevel/100) and caps morale recovery. Decays
    /// extremely slowly (handled in <c>MoraleRecoveryStep</c>) so a hit province is
    /// economically crippled for the rest of the game without being literally dead.
    /// </summary>
    public int RadiationLevel { get; set; }

    public int BasePopulation { get; set; }

    // Base resource output per tick. Buildings add multipliers on top during ResourceProductionStep.
    public int MoneyPerTick { get; set; }
    public int OilPerTick { get; set; }
    public int SteelPerTick { get; set; }
    public int ElectronicsPerTick { get; set; }
    public int FoodPerTick { get; set; }
    public int ManpowerPerTick { get; set; }

    public ICollection<Building> Buildings { get; set; } = new List<Building>();
    public ICollection<Unit> UnitsStationed { get; set; } = new List<Unit>();
}
