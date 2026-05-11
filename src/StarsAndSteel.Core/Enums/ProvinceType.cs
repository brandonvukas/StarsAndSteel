namespace StarsAndSteel.Core.Enums;

public enum ProvinceType
{
    Urban = 0,
    Industrial = 1,
    Tech = 2,
    Agricultural = 3,
    Resource = 4,
    Capital = 5,
    /// <summary>
    /// Open-ocean province. Forward-compat for true sea tiles; not seeded in MVP
    /// (Phase 2I uses sea-crossing edges between coastal land provinces instead).
    /// </summary>
    Sea = 6
}
