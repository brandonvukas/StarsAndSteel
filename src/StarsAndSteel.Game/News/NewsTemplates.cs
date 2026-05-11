using StarsAndSteel.Core.Enums;
using StarsAndSteel.Game.Tick;

namespace StarsAndSteel.Game.News;

/// <summary>
/// Template variants for a single news category. <see cref="Severity"/> and
/// <see cref="Category"/> are baked into the template (not picked per event)
/// so the cable-news tone stays consistent — a province capture is always
/// Breaking/Combat, an air strike is always Notable/Combat, etc.
/// <para/>
/// Variants are placeholder-substituted by <see cref="NewsTemplates.Render"/>
/// using <c>{key}</c> tokens. A missing token leaves the literal text in
/// place — a deliberately non-throwing failure mode so a typo in a template
/// never stops a tick from completing.
/// </summary>
public sealed record NewsTemplate(
    NewsSeverity Severity,
    NewsCategory Category,
    IReadOnlyList<string> HeadlineVariants,
    IReadOnlyList<string> BodyVariants);

/// <summary>
/// Static catalogue of cable-news templates per <c>docs/07-GAME-LOOP.md</c> §"NewsStep".
/// Pure: variant selection takes an <see cref="IRandomSource"/> so replays produce the
/// exact same headlines.
/// <para/>
/// MVP coverage: ProvinceCaptured (Breaking), AirStrikeResolved (Notable),
/// CombatResolved (Notable), UnitBuilt (Info), BuildingCompleted (Info).
/// </summary>
public static class NewsTemplates
{
    public static readonly NewsTemplate ProvinceCaptured = new(
        Severity: NewsSeverity.Breaking,
        Category: NewsCategory.Combat,
        HeadlineVariants: new[]
        {
            "BREAKING: {attacker} forces seize {province} — {defender} in retreat",
            "{province} FALLS — {defender} command issues no comment",
            "STARS RISING OVER {province}: {attacker} flag raised at dawn",
        },
        BodyVariants: new[]
        {
            "Frontline reports indicate {attacker} units overran {defender} positions in {province} during the latest engagement.",
            "Analysts say the loss of {province} reshapes the regional balance of power.",
        });

    public static readonly NewsTemplate AirStrikeResolved = new(
        Severity: NewsSeverity.Notable,
        Category: NewsCategory.Combat,
        HeadlineVariants: new[]
        {
            "{attacker} drone swarm strikes {province} — heavy casualties reported",
            "Pentagon source: {attacker} bombers crossed into {province} airspace overnight",
            "Anti-air alarms blare in {province} as {attacker} aircraft press home strike",
        },
        BodyVariants: new[]
        {
            "Initial damage assessments are still coming in from {province}.",
            "{attacker} command has not commented on the operation.",
        });

    public static readonly NewsTemplate CombatResolved = new(
        Severity: NewsSeverity.Notable,
        Category: NewsCategory.Combat,
        HeadlineVariants: new[]
        {
            "Heavy fighting in {province} — both sides take losses",
            "Skirmish on the {province} line — outcome inconclusive",
            "{attacker} probe of {province} repulsed at dusk",
        },
        BodyVariants: new[]
        {
            "Forces from {attacker} clashed with {defender} units throughout the day.",
            "Smoke continues to rise from {province} as the dust settles.",
        });

    public static readonly NewsTemplate UnitBuilt = new(
        Severity: NewsSeverity.Info,
        Category: NewsCategory.Politics,
        HeadlineVariants: new[]
        {
            "{owner} commissions new {unitType} stack at {province}",
            "Recruitment milestone: {owner} fields fresh {unitType} battalion in {province}",
        },
        BodyVariants: new[]
        {
            "The new formation is reported at full strength.",
        });

    public static readonly NewsTemplate BuildingCompleted = new(
        Severity: NewsSeverity.Info,
        Category: NewsCategory.Economy,
        HeadlineVariants: new[]
        {
            "{owner} opens new {buildingType} in {province}",
            "Ribbon-cutting in {province}: {owner} brings {buildingType} online",
        },
        BodyVariants: new[]
        {
            "Local officials hail the project as a boost to regional output.",
        });

    public static readonly NewsTemplate VictoryAchieved = new(
        Severity: NewsSeverity.Breaking,
        Category: NewsCategory.Politics,
        HeadlineVariants: new[]
        {
            "VICTORY DECLARED: {winner} achieves total domination ({owned}/{total} provinces)",
            "WAR'S END: {winner} stands alone — {owned} of {total} provinces under their flag",
            "BREAKING: {winner} secures {owned}/{total} provinces, war effectively over",
        },
        BodyVariants: new[]
        {
            "Capitals worldwide acknowledge {winner} as the dominant power on the map.",
            "Analysts call the {winner} campaign a textbook total-domination victory.",
        });

    public static readonly NewsTemplate PlayerEliminated = new(
        Severity: NewsSeverity.Notable,
        Category: NewsCategory.Politics,
        HeadlineVariants: new[]
        {
            "{nation} ELIMINATED — last province falls",
            "End of the line for {nation}: government in exile after final defeat",
        },
        BodyVariants: new[]
        {
            "Diplomats say {nation}'s remaining forces have laid down arms.",
        });

    public static readonly NewsTemplate CoalitionVictoryAchieved = new(
        Severity: NewsSeverity.Breaking,
        Category: NewsCategory.Politics,
        HeadlineVariants: new[]
        {
            "COALITION VICTORY: {coalition} jointly secure {owned}/{total} provinces",
            "ALLIANCE TRIUMPHS: {coalition} declared co-victors of the war",
            "BREAKING: {coalition} bloc wins the war — {owned} of {total} provinces under coalition control",
        },
        BodyVariants: new[]
        {
            "World capitals recognize the {coalition} alliance as the dominant bloc.",
            "Analysts call it the largest coalition victory in the campaign's history.",
        });

    /// <summary>
    /// Pick a variant deterministically from <paramref name="variants"/> via the per-world RNG.
    /// Empty list returns empty string so the caller never crashes on a misconfigured template.
    /// </summary>
    public static string PickVariant(IReadOnlyList<string> variants, IRandomSource rng)
    {
        ArgumentNullException.ThrowIfNull(variants);
        ArgumentNullException.ThrowIfNull(rng);
        if (variants.Count == 0) return string.Empty;
        var idx = rng.NextInt(variants.Count);
        return variants[idx];
    }

    /// <summary>
    /// Substitute <c>{key}</c> tokens in <paramref name="template"/> with values from
    /// <paramref name="values"/>. Missing keys are left as the literal token (visible in QA
    /// without breaking the tick). Case-sensitive on the keys.
    /// </summary>
    public static string Render(string template, IReadOnlyDictionary<string, string> values)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(values);
        if (template.Length == 0) return template;

        // Manual scanner avoids regex overhead inside the per-tick hot path.
        var sb = new System.Text.StringBuilder(template.Length + 32);
        var i = 0;
        while (i < template.Length)
        {
            var c = template[i];
            if (c == '{')
            {
                var end = template.IndexOf('}', i + 1);
                if (end > i + 1)
                {
                    var key = template.Substring(i + 1, end - i - 1);
                    if (values.TryGetValue(key, out var replacement))
                    {
                        sb.Append(replacement);
                    }
                    else
                    {
                        // Leave the literal token so QA can see what the step asked for.
                        sb.Append('{').Append(key).Append('}');
                    }
                    i = end + 1;
                    continue;
                }
            }
            sb.Append(c);
            i++;
        }
        return sb.ToString();
    }
}
