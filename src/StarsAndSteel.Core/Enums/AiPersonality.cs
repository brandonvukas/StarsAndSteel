namespace StarsAndSteel.Core.Enums;

/// <summary>
/// Personality archetype for AI players. Drives priority-list selection in <c>AiTurnStep</c>.
/// MVP ships only <see cref="Hawk"/>; the rest land in Phase 2.
/// </summary>
public enum AiPersonality
{
    Hawk = 0,
    Industrialist = 1,
    Isolationist = 2,
    Schemer = 3,
    Insurgent = 4
}
