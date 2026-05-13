using StarsAndSteel.Core.Enums;

namespace StarsAndSteel.Game.Combat;

/// <summary>
/// Per-unit-type combat parameters and the damage-tier interaction matrix from
/// <c>docs/04-GAME-MECHANICS.md</c> §"Combat (combined arms)".
/// <para/>
/// Damage tiers (★ → ★★★) map to flat damage fractions of attacker effective
/// strength applied to the target stack's Strength. The numbers below are the
/// MVP balance pass; rebalance after playtests.
/// <list type="bullet">
///   <item>★ (low)        → 0.05 fraction</item>
///   <item>★★ (medium)    → 0.10 fraction</item>
///   <item>★★★ (devastating) → 0.20 fraction</item>
///   <item>— (cannot engage) → 0</item>
/// </list>
/// <para/>
/// Per-type "strength" is a multiplier on stack Strength when computing effective
/// strength: a 1000-strong tank stack hits like a 1500 mech-infantry stack because
/// MBT.UnitTypeStrength = 1.5. These numbers are deliberately compressed so the
/// matrix dominates outcomes.
/// </summary>
public static class CombatStats
{
    /// <summary>Damage fraction applied when this attacker engages this target.</summary>
    public static double DamageFraction(UnitType attacker, UnitType target) =>
        Matrix.TryGetValue((attacker, target), out var t) ? TierToFraction(t) : 0.0;

    private static double TierToFraction(int tier) => tier switch
    {
        1 => 0.05,
        2 => 0.10,
        3 => 0.20,
        _ => 0.0,
    };

    /// <summary>
    /// Per-type effective-strength multiplier (docs/04 formula term <c>unitTypeStrength</c>).
    /// </summary>
    public static double UnitTypeStrength(UnitType type) => type switch
    {
        UnitType.MechInfantry      => 1.0,
        UnitType.NationalGuard     => 0.7,
        UnitType.SpecialForces     => 1.4,
        UnitType.MainBattleTank    => 1.5,
        UnitType.MobileArtillery   => 1.3,
        UnitType.AABattery         => 0.6, // weak in ground combat; matrix shows it can't engage ground
        UnitType.ReconDrone        => 0.2,
        UnitType.CombatDrone       => 1.0,
        UnitType.AttackHelicopter  => 1.4,
        UnitType.MultiroleFighter  => 1.6,
        UnitType.StrategicBomber   => 1.8,
        UnitType.StealthBomber     => 2.2,
        // Phase 3b: small fast recon platform; weak in combat but valuable for the
        // (deferred) sight model — kept above ReconDrone but well below MultiroleFighter.
        UnitType.StealthDrone      => 0.5,
        UnitType.Frigate           => 1.4,
        UnitType.Destroyer         => 1.8,
        // Phase 2b: a carrier is a fat low-DPS hull. Survivability comes from its
        // escort ships and embarked wings, not from its own gun. Wings are "elite"
        // multirole air — slightly above MultiroleFighter to reflect their
        // omni-role training.
        UnitType.AircraftCarrier   => 2.5,
        UnitType.CarrierAirWing    => 1.7,
        _ => 1.0,
    };

    /// <summary>
    /// docs/04 matrix as (attacker, target) → tier (1=★, 2=★★, 3=★★★). Missing entry = "—" = 0 dmg.
    /// </summary>
    private static readonly IReadOnlyDictionary<(UnitType Attacker, UnitType Target), int> Matrix = Build();

    private static IReadOnlyDictionary<(UnitType, UnitType), int> Build()
    {
        var m = new Dictionary<(UnitType, UnitType), int>();

        // The matrix in docs/04 is keyed off the five MVP units (MechInf, MBT, Art, AA, Drone,
        // Fighter, Helo, Bomber). NationalGuard and SpecialForces aren't tabled there because
        // they're Phase 2; for Phase 1I we treat them as MechInf-class targets and attackers,
        // which keeps the catalogue exhaustive and avoids "0 damage to NG stack" surprises.
        var infantryClass = new[] { UnitType.MechInfantry, UnitType.NationalGuard, UnitType.SpecialForces };
        var bomberClass = new[] { UnitType.StrategicBomber, UnitType.StealthBomber };

        // Helper: add (a, t) → tier for every a in attackers and every t in targets.
        void Add(IEnumerable<UnitType> attackers, IEnumerable<UnitType> targets, int tier)
        {
            foreach (var a in attackers)
                foreach (var t in targets)
                    m[(a, t)] = tier;
        }

        // MechInf-class →
        Add(infantryClass, infantryClass,             2);
        Add(infantryClass, new[] { UnitType.MainBattleTank }, 1);
        Add(infantryClass, new[] { UnitType.MobileArtillery }, 1);
        Add(infantryClass, new[] { UnitType.AABattery },     2);
        // MBT →
        Add(new[] { UnitType.MainBattleTank }, infantryClass,                3);
        Add(new[] { UnitType.MainBattleTank }, new[] { UnitType.MainBattleTank },   3);
        Add(new[] { UnitType.MainBattleTank }, new[] { UnitType.MobileArtillery },  2);
        Add(new[] { UnitType.MainBattleTank }, new[] { UnitType.AABattery },        2);
        // Artillery →
        Add(new[] { UnitType.MobileArtillery }, infantryClass,                3);
        Add(new[] { UnitType.MobileArtillery }, new[] { UnitType.MainBattleTank },   2);
        Add(new[] { UnitType.MobileArtillery }, new[] { UnitType.MobileArtillery },  1);
        Add(new[] { UnitType.MobileArtillery }, new[] { UnitType.AABattery },        3);
        // AA → (only air targets)
        Add(new[] { UnitType.AABattery }, new[] { UnitType.ReconDrone },       3);
        Add(new[] { UnitType.AABattery }, new[] { UnitType.CombatDrone },      3);
        Add(new[] { UnitType.AABattery }, new[] { UnitType.AttackHelicopter }, 3);
        Add(new[] { UnitType.AABattery }, new[] { UnitType.MultiroleFighter }, 2);
        Add(new[] { UnitType.AABattery }, bomberClass,                          3);
        // CombatDrone →  (matrix row "Drone")
        Add(new[] { UnitType.CombatDrone }, infantryClass,                2);
        Add(new[] { UnitType.CombatDrone }, new[] { UnitType.MainBattleTank },   2);
        Add(new[] { UnitType.CombatDrone }, new[] { UnitType.MobileArtillery },  3);
        Add(new[] { UnitType.CombatDrone }, new[] { UnitType.AABattery },        1);
        // Fighter → (only air targets)
        Add(new[] { UnitType.MultiroleFighter }, new[] { UnitType.ReconDrone },       3);
        Add(new[] { UnitType.MultiroleFighter }, new[] { UnitType.CombatDrone },      3);
        Add(new[] { UnitType.MultiroleFighter }, new[] { UnitType.AttackHelicopter }, 3);
        Add(new[] { UnitType.MultiroleFighter }, new[] { UnitType.MultiroleFighter }, 3);
        Add(new[] { UnitType.MultiroleFighter }, bomberClass,                          3);
        // Helo →
        Add(new[] { UnitType.AttackHelicopter }, infantryClass,                3);
        Add(new[] { UnitType.AttackHelicopter }, new[] { UnitType.MainBattleTank },   3);
        Add(new[] { UnitType.AttackHelicopter }, new[] { UnitType.MobileArtillery },  2);
        Add(new[] { UnitType.AttackHelicopter }, new[] { UnitType.AABattery },        1);
        // Bomber →
        Add(bomberClass, infantryClass,                3);
        Add(bomberClass, new[] { UnitType.MainBattleTank },   3);
        Add(bomberClass, new[] { UnitType.MobileArtillery },  3);
        Add(bomberClass, new[] { UnitType.AABattery },        2);

        // Naval (Phase 2I MVP-lite). Naval-vs-naval is the primary interaction; in
        // the absence of true sea tiles, naval stacks only co-locate with each other
        // when one moves into a coastal province occupied by an opposing naval stack.
        // We also model frigates' AA suite (vs air) and bombers' anti-ship capability
        // so a coastal raid scenario resolves sensibly.
        var navalClass = new[] { UnitType.Frigate, UnitType.Destroyer };
        // Frigate → naval (escort role)
        Add(new[] { UnitType.Frigate },   navalClass, 2);
        Add(new[] { UnitType.Destroyer }, navalClass, 3);
        // Frigate → air (point-defense AA)
        Add(new[] { UnitType.Frigate }, new[] { UnitType.ReconDrone, UnitType.CombatDrone, UnitType.AttackHelicopter }, 2);
        Add(new[] { UnitType.Frigate }, new[] { UnitType.MultiroleFighter }, 1);
        Add(new[] { UnitType.Frigate }, bomberClass, 2);
        // Destroyer → air (heavier AA suite)
        Add(new[] { UnitType.Destroyer }, new[] { UnitType.ReconDrone, UnitType.CombatDrone, UnitType.AttackHelicopter }, 3);
        Add(new[] { UnitType.Destroyer }, new[] { UnitType.MultiroleFighter }, 2);
        Add(new[] { UnitType.Destroyer }, bomberClass, 3);
        // Air → naval (anti-ship)
        Add(bomberClass, navalClass, 3);
        Add(new[] { UnitType.CombatDrone }, navalClass, 2);
        Add(new[] { UnitType.AttackHelicopter }, navalClass, 2);
        Add(new[] { UnitType.MultiroleFighter }, navalClass, 1);

        // Phase 2b: Naval Aviation. AircraftCarrier acts mostly as a platform; it has
        // light point-defense AA but cannot meaningfully hurt other ships on its own.
        // CarrierAirWing fights as an elite multirole — strong vs all air, strong
        // anti-ship (it's the carrier's main gun), and capable bombing of ground.
        var navalClassWithCarrier = new[] { UnitType.Frigate, UnitType.Destroyer, UnitType.AircraftCarrier };
        // Carrier light AA
        Add(new[] { UnitType.AircraftCarrier }, new[] { UnitType.ReconDrone, UnitType.CombatDrone, UnitType.AttackHelicopter }, 1);
        Add(new[] { UnitType.AircraftCarrier }, new[] { UnitType.MultiroleFighter, UnitType.CarrierAirWing }, 1);
        Add(new[] { UnitType.AircraftCarrier }, bomberClass, 1);
        // Existing-vs-carrier: carriers are valid naval targets for everything that
        // already engages naval. Re-broadcast the entries to include the carrier.
        Add(new[] { UnitType.Frigate, UnitType.Destroyer }, new[] { UnitType.AircraftCarrier }, 2);
        Add(bomberClass,                          new[] { UnitType.AircraftCarrier }, 3);
        Add(new[] { UnitType.CombatDrone, UnitType.AttackHelicopter }, new[] { UnitType.AircraftCarrier }, 2);
        Add(new[] { UnitType.MultiroleFighter },  new[] { UnitType.AircraftCarrier }, 1);
        // CarrierAirWing as attacker — fighter+bomber hybrid.
        Add(new[] { UnitType.CarrierAirWing }, infantryClass,                 3);
        Add(new[] { UnitType.CarrierAirWing }, new[] { UnitType.MainBattleTank },   2);
        Add(new[] { UnitType.CarrierAirWing }, new[] { UnitType.MobileArtillery },  2);
        Add(new[] { UnitType.CarrierAirWing }, new[] { UnitType.AABattery },        2);
        Add(new[] { UnitType.CarrierAirWing }, new[] { UnitType.ReconDrone, UnitType.CombatDrone, UnitType.AttackHelicopter }, 3);
        Add(new[] { UnitType.CarrierAirWing }, new[] { UnitType.MultiroleFighter, UnitType.CarrierAirWing }, 3);
        Add(new[] { UnitType.CarrierAirWing }, bomberClass,                          3);
        Add(new[] { UnitType.CarrierAirWing }, navalClassWithCarrier,                3);
        // Anti-air vs CarrierAirWing — same tiers as MultiroleFighter (mid-tier interceptor).
        Add(new[] { UnitType.AABattery },        new[] { UnitType.CarrierAirWing }, 2);
        Add(new[] { UnitType.MultiroleFighter }, new[] { UnitType.CarrierAirWing }, 3);
        Add(new[] { UnitType.Frigate },          new[] { UnitType.CarrierAirWing }, 1);
        Add(new[] { UnitType.Destroyer },        new[] { UnitType.CarrierAirWing }, 2);

        return m;
    }

    /// <summary>True if this unit type is part of the "anti-air screen" for the combined-arms bonus.</summary>
    public static bool IsAntiAir(UnitType t) => t == UnitType.AABattery;

    /// <summary>True if this unit type counts as ground for combined-arms bonus.</summary>
    public static bool IsGround(UnitType t) =>
        t is UnitType.MechInfantry or UnitType.NationalGuard or UnitType.SpecialForces
          or UnitType.MainBattleTank or UnitType.MobileArtillery;

    /// <summary>True if this unit type counts as air for combined-arms bonus.</summary>
    public static bool IsAir(UnitType t) =>
        t is UnitType.ReconDrone or UnitType.CombatDrone or UnitType.AttackHelicopter
          or UnitType.MultiroleFighter or UnitType.StrategicBomber or UnitType.StealthBomber
          or UnitType.StealthDrone or UnitType.CarrierAirWing;

    /// <summary>True if this unit type is a naval combatant (Phase 2I/2b).</summary>
    public static bool IsNaval(UnitType t) =>
        t is UnitType.Frigate or UnitType.Destroyer or UnitType.AircraftCarrier;

    /// <summary>Stealth-bomber bypass-AA roll target (60% bypass). Phase 1: no research bonus.</summary>
    public const double StealthBypassChance = 0.60;
}
