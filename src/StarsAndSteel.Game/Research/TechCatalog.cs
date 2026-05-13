namespace StarsAndSteel.Game.Research;

/// <summary>
/// Static catalogue of techs available in the Phase 2G research tree (12 techs).
/// Pure data; lives in code (not DB) so we can rebalance without migrations
/// (per docs/03 ResearchProgress note: <c>TechId</c> is a code-side string key).
/// <para/>
/// Categories (3 each): Military / Industry / Doctrine / Logistics. Each tech has
/// a one-time research cost (Money + Electronics) and a flat duration in ticks.
/// Effects are advisory MVP — most are documented in <see cref="TechSpec.Summary"/>
/// and consumed by Phase 3+ steps; the immediate gameplay value of unlocking a
/// tech in Phase 2 is the visible progress + future-proofing of save data.
/// <see cref="TechSpec.Prerequisites"/> form a small DAG so the client can render
/// an actual tree.
/// </summary>
public sealed record TechSpec(
    string Id,
    string Name,
    string Category,
    string Summary,
    long MoneyCost,
    long ElectronicsCost,
    int TicksToResearch,
    IReadOnlyList<string> Prerequisites);

public static class TechCatalog
{
    private static readonly TechSpec[] _all =
    {
        // Military (cheaper Tier 1, gated Tier 2)
        new("advanced_armor", "Advanced Armor", "Military",
            "+5% effective strength for ground units (Phase 3 hook).",
            MoneyCost: 1500, ElectronicsCost: 200, TicksToResearch: 12,
            Prerequisites: Array.Empty<string>()),
        new("smart_munitions", "Smart Munitions", "Military",
            "+10% air-strike damage to ground stacks (Phase 3 hook).",
            MoneyCost: 1500, ElectronicsCost: 400, TicksToResearch: 14,
            Prerequisites: Array.Empty<string>()),
        new("stealth_systems", "Stealth Systems", "Military",
            "Unlocks Stealth Bomber recruitment (existing UnitType).",
            MoneyCost: 4000, ElectronicsCost: 1500, TicksToResearch: 24,
            Prerequisites: new[] { "smart_munitions" }),
        // Phase 3b: lighter sister tech to stealth_systems — unlocks the StealthDrone
        // unit. Cheaper / faster than full stealth bombers because the drone is a
        // recon-class platform, not a strategic strike asset.
        new("stealth_drones", "Stealth Drones", "Military",
            "Unlocks Stealth Drone recon platform.",
            MoneyCost: 2500, ElectronicsCost: 1000, TicksToResearch: 18,
            Prerequisites: new[] { "smart_munitions" }),
        // Phase 3c: unlocks the Submarine naval unit. Subs are stealth platforms
        // (hidden in enemy snapshots unless an enemy Frigate/Destroyer is co-located)
        // and devastating vs surface ships; surface ships need ASW (Frigate/Destroyer)
        // even to engage them at all. Comparable cost/duration to stealth_systems.
        new("submarine_warfare", "Submarine Warfare", "Military",
            "Unlocks Submarine recruitment at NavalYards. Stealth + anti-ship.",
            MoneyCost: 3500, ElectronicsCost: 1200, TicksToResearch: 22,
            Prerequisites: new[] { "advanced_armor" }),
        // Phase 3d: unlocks the player-level CyberAttack order. Requires a
        // CyberOperationsCenter at the launch province; drains money or slows
        // research at the target. Sits in Doctrine because cyber sabotage is
        // an organizational capability, not a hardware platform.
        new("cyber_warfare", "Cyber Warfare", "Doctrine",
            "Unlocks the CyberAttack order. Launch from a CyberOperationsCenter.",
            MoneyCost: 2000, ElectronicsCost: 1000, TicksToResearch: 16,
            Prerequisites: new[] { "combined_arms" }),

        // Industry
        new("modular_construction", "Modular Construction", "Industry",
            "−10% building TicksToBuild (Phase 3 hook).",
            MoneyCost: 1200, ElectronicsCost: 200, TicksToResearch: 10,
            Prerequisites: Array.Empty<string>()),
        new("mass_production", "Mass Production", "Industry",
            "−10% unit construction cost (Phase 3 hook).",
            MoneyCost: 1500, ElectronicsCost: 300, TicksToResearch: 12,
            Prerequisites: new[] { "modular_construction" }),
        new("automated_factories", "Automated Factories", "Industry",
            "+15% production from Steel Mills + Refineries (Phase 3 hook).",
            MoneyCost: 3000, ElectronicsCost: 800, TicksToResearch: 20,
            Prerequisites: new[] { "mass_production" }),

        // Doctrine
        new("combined_arms", "Combined Arms", "Doctrine",
            "Combined-arms bonus rises from +20% → +25% in CombatStep (Phase 3g).",
            MoneyCost: 1200, ElectronicsCost: 100, TicksToResearch: 10,
            Prerequisites: Array.Empty<string>()),
        new("defense_in_depth", "Defense in Depth", "Doctrine",
            "Defending side gains +10% effective strength + outgoing damage in CombatStep (Phase 3g).",
            MoneyCost: 1500, ElectronicsCost: 200, TicksToResearch: 12,
            Prerequisites: new[] { "combined_arms" }),
        new("maneuver_warfare", "Maneuver Warfare", "Doctrine",
            "Reserved: meant to shave 1 tick off cheap-terrain moves; deferred until multi-tick movement (movement is single-tick MVP).",
            MoneyCost: 2500, ElectronicsCost: 500, TicksToResearch: 18,
            Prerequisites: new[] { "combined_arms" }),

        // Logistics
        new("rail_network", "Rail Network", "Logistics",
            "Logistics-network bonus rises from +10% → +15%.",
            MoneyCost: 1500, ElectronicsCost: 200, TicksToResearch: 12,
            Prerequisites: Array.Empty<string>()),
        new("strategic_airlift", "Strategic Airlift", "Logistics",
            "Air units may use any owned Air Base as a temporary base (Phase 3).",
            MoneyCost: 2500, ElectronicsCost: 600, TicksToResearch: 18,
            Prerequisites: new[] { "rail_network" }),
        new("global_supply_chain", "Global Supply Chain", "Logistics",
            "Upkeep on units in own territory −20% (Phase 3 hook).",
            MoneyCost: 3500, ElectronicsCost: 800, TicksToResearch: 22,
            Prerequisites: new[] { "rail_network" }),
    };

    public static IReadOnlyList<TechSpec> All => _all;

    public static TechSpec? Find(string id) =>
        _all.FirstOrDefault(t => string.Equals(t.Id, id, StringComparison.Ordinal));

    public static bool Exists(string id) => Find(id) is not null;
}
