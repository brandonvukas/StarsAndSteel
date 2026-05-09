using StarsAndSteel.Core.Enums;

namespace StarsAndSteel.Api.Hubs.Dtos;

/// <summary>
/// Wire-format records broadcast by <see cref="GameHub"/> after each tick. Kept
/// separate from <c>StarsAndSteel.Game.Tick.Events.*</c> so the Game project
/// stays free of any SignalR / serialization concerns and the wire shape can
/// evolve independently of the in-process event types.
/// <para/>
/// All payloads include <c>Tick</c> so a client that briefly drops a packet can
/// detect a gap and re-snapshot (per docs/06 §"Hub semantics").
/// <para/>
/// Phase 1J scope: per-world group broadcast for everything. Per-player events
/// (resources, fog-aware unit positions) are broadcast to the world too — the
/// client filters in Phase 1K when fog-of-war is enforced over the wire.
/// </summary>
public static class TickEventDtos
{
    /// <summary>
    /// Sent once per successful tick. Always the LAST event for the tick so
    /// the client can use it as a barrier to apply queued diffs atomically.
    /// </summary>
    public sealed record TickAdvanced(int Tick, int EventCount);

    public sealed record ResourcesUpdated(
        int Tick,
        Guid PlayerId,
        long MoneyDelta,
        long OilDelta,
        long SteelDelta,
        long ElectronicsDelta,
        long FoodDelta,
        long ManpowerDelta);

    public sealed record UnitMoved(
        int Tick,
        Guid UnitId,
        Guid OwnerPlayerId,
        Guid FromProvinceId,
        Guid ToProvinceId);

    public sealed record UnitDestroyed(
        int Tick,
        Guid UnitId,
        Guid OwnerPlayerId,
        Guid? LocationProvinceId,
        string Cause);

    public sealed record AirStrikeResolved(
        int Tick,
        Guid AttackerUnitId,
        Guid AttackerPlayerId,
        Guid TargetProvinceId,
        int AttackerStrengthLoss,
        int DefenderStrengthLoss);

    public sealed record CombatResolved(
        int Tick,
        Guid ProvinceId,
        Guid AttackerPlayerId,
        Guid DefenderPlayerId,
        int AttackerStrengthLoss,
        int DefenderStrengthLoss,
        Guid? WinnerPlayerId);

    public sealed record ProvinceCaptured(
        int Tick,
        Guid ProvinceId,
        Guid? FromPlayerId,
        Guid ToPlayerId);

    public sealed record UnitBuilt(
        int Tick,
        Guid UnitId,
        Guid OwnerPlayerId,
        Guid ProvinceId,
        UnitType Type,
        int Strength);

    public sealed record BuildingCompleted(
        int Tick,
        Guid BuildingId,
        Guid OwnerPlayerId,
        Guid ProvinceId,
        BuildingType Type,
        int Level);
}

/// <summary>
/// String constants for the server-to-client method names. Kept in one place so
/// a client TypeScript wrapper can mirror them without spelling drift.
/// </summary>
public static class TickEventNames
{
    public const string TickAdvanced = "TickAdvanced";
    public const string ResourcesUpdated = "ResourcesUpdated";
    public const string UnitMoved = "UnitMoved";
    public const string UnitDestroyed = "UnitDestroyed";
    public const string AirStrikeResolved = "AirStrikeResolved";
    public const string CombatResolved = "CombatResolved";
    public const string ProvinceCaptured = "ProvinceCaptured";
    public const string UnitBuilt = "UnitBuilt";
    public const string BuildingCompleted = "BuildingCompleted";
}
