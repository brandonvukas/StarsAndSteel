using FluentAssertions;
using StarsAndSteel.Game.News;
using StarsAndSteel.Game.Tick;

namespace StarsAndSteel.Tests.Game.News;

public class NewsTemplatesTests
{
    [Fact]
    public void Render_substitutes_known_tokens()
    {
        var rendered = NewsTemplates.Render(
            "{a} attacks {b} in {c}",
            new Dictionary<string, string>
            {
                ["a"] = "USA",
                ["b"] = "Canada",
                ["c"] = "Quebec",
            });

        rendered.Should().Be("USA attacks Canada in Quebec");
    }

    [Fact]
    public void Render_leaves_unknown_tokens_as_literal_text()
    {
        // Deliberate non-throwing failure mode — a typo in a template should never
        // poison a tick; QA sees the literal {missing} and fixes it.
        var rendered = NewsTemplates.Render(
            "{known} {missing}",
            new Dictionary<string, string> { ["known"] = "ok" });

        rendered.Should().Be("ok {missing}");
    }

    [Fact]
    public void Render_handles_braces_at_end_of_string_without_throwing()
    {
        var rendered = NewsTemplates.Render(
            "trailing {",
            new Dictionary<string, string>());

        rendered.Should().Be("trailing {");
    }

    [Fact]
    public void PickVariant_is_deterministic_for_same_seed()
    {
        var variants = new[] { "a", "b", "c", "d", "e" };
        var rng1 = new DeterministicRandom(42);
        var rng2 = new DeterministicRandom(42);

        var s1 = string.Concat(Enumerable.Range(0, 10).Select(_ => NewsTemplates.PickVariant(variants, rng1)));
        var s2 = string.Concat(Enumerable.Range(0, 10).Select(_ => NewsTemplates.PickVariant(variants, rng2)));

        s1.Should().Be(s2);
    }

    [Fact]
    public void PickVariant_returns_empty_for_empty_list()
    {
        NewsTemplates.PickVariant(Array.Empty<string>(), new DeterministicRandom(1))
            .Should().BeEmpty();
    }
}
