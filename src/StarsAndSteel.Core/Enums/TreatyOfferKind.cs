namespace StarsAndSteel.Core.Enums;

/// <summary>
/// Kind of treaty being proposed. Maps to the <see cref="DiplomaticStatus"/> the relation will
/// transition to if the offer is accepted (Peace = end-of-war, NonAggression / Allied as named).
/// War declarations are not offers — they take effect immediately and bypass this table.
/// </summary>
public enum TreatyOfferKind
{
    Peace = 0,
    NonAggression = 1,
    Alliance = 2
}
