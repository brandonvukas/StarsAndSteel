using StarsAndSteel.Core.Entities;
using StarsAndSteel.Core.Enums;

namespace StarsAndSteel.Game.Diplomacy;

/// <summary>
/// Read-only O(1) lookup over a world's <see cref="DiplomaticRelation"/> rows. Tick steps
/// receive an instance of this through <see cref="Tick.TickContext.Relations"/> and use
/// <see cref="GetStatus"/> to gate combat / movement / air strikes against the current
/// state of diplomacy.
/// <para/>
/// Canonicalization: rows are written in symmetric pairs (A→B and B→A) by
/// <see cref="DiplomacyService"/>, but a query may legitimately see only one direction during
/// the brief window of an in-flight transaction. We collapse on construction by indexing on
/// an ordered <c>(min, max)</c> Guid pair, so either direction returns the same status.
/// <para/>
/// MVP default policy: a pair with NO row in the table is treated as implicitly hostile
/// (<see cref="IsHostile"/> returns true). Diplomacy is opt-in — until two players sign
/// peace / NAP / alliance, combat between them is unconstrained, matching the docs/04 model
/// of a world where war is the natural state and treaties are the exception. Self-vs-self
/// always returns Peace and is treated as friendly.
/// </summary>
public sealed class RelationLookup
{
    private readonly Dictionary<(Guid, Guid), DiplomaticStatus> _byPair;
    private readonly Dictionary<Guid, int> _inboundSanctionCount;

    public static RelationLookup Empty { get; } = new(Array.Empty<DiplomaticRelation>());

    public RelationLookup(IEnumerable<DiplomaticRelation> relations)
    {
        ArgumentNullException.ThrowIfNull(relations);
        _byPair = new Dictionary<(Guid, Guid), DiplomaticStatus>();
        _inboundSanctionCount = new Dictionary<Guid, int>();
        foreach (var r in relations)
        {
            var key = OrderedPair(r.FromPlayerId, r.ToPlayerId);
            // Last writer wins on duplicates — the symmetric pair should agree, but if two
            // rows disagree the table is internally inconsistent and we just pick one.
            _byPair[key] = r.Status;

            // Phase 4e: directional sanction tally. Each row that flags IsSanctioning bumps
            // a counter against the From→To target. ResourceProductionStep multiplies the
            // target's money pool by (1 - 0.25 * count) with a floor at 0.25.
            if (r.IsSanctioning)
            {
                _inboundSanctionCount[r.ToPlayerId] =
                    _inboundSanctionCount.GetValueOrDefault(r.ToPlayerId) + 1;
            }
        }
    }

    /// <summary>
    /// Returns the explicit diplomatic status between two players, or <c>null</c> if no
    /// relation row exists for the pair. Order-independent.
    /// </summary>
    public DiplomaticStatus? GetExplicitStatus(Guid playerA, Guid playerB)
    {
        if (playerA == playerB) return DiplomaticStatus.Peace;
        var key = OrderedPair(playerA, playerB);
        return _byPair.TryGetValue(key, out var status) ? status : null;
    }

    /// <summary>
    /// True iff hostilities are permitted between the pair. Hostility means either an
    /// explicit War relation OR no relation row at all (the implicit-hostility default).
    /// Returns false for any explicit Peace / NonAggression / Alliance / TradeAgreement row,
    /// and false for self.
    /// </summary>
    public bool IsHostile(Guid playerA, Guid playerB)
    {
        if (playerA == playerB) return false;
        var explicitStatus = GetExplicitStatus(playerA, playerB);
        return explicitStatus is null or DiplomaticStatus.War;
    }

    /// <summary>True iff the pair is explicitly <see cref="DiplomaticStatus.Allied"/>.</summary>
    public bool AreAllied(Guid playerA, Guid playerB) =>
        GetExplicitStatus(playerA, playerB) == DiplomaticStatus.Allied;

    /// <summary>
    /// Phase 4e: number of OTHER players currently sanctioning <paramref name="targetPlayerId"/>
    /// (count of <see cref="DiplomaticRelation"/> rows where <c>ToPlayerId == targetPlayerId</c>
    /// and <c>IsSanctioning == true</c>). Returns 0 when no sanctions are active.
    /// </summary>
    public int CountInboundSanctions(Guid targetPlayerId) =>
        _inboundSanctionCount.GetValueOrDefault(targetPlayerId);

    private static (Guid, Guid) OrderedPair(Guid x, Guid y) =>
        x.CompareTo(y) <= 0 ? (x, y) : (y, x);
}
