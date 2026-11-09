using System.Collections.Concurrent;
using System.Collections.Immutable;
using Tactics.Logic;
using Tactics.Presentation.Beats;
namespace Tactics.Presentation.Tests.Board;

/// <summary>Reuse immutable real-engine samples; each distinct pack instance keeps its own results.</summary>
internal static class ShowcaseFixtures
{
    private static readonly ConcurrentDictionary<(RulesPack Rules, string Class), Lazy<ImmutableArray<FxShowcaseSample>>> Samples = new();
    public static ImmutableArray<FxShowcaseSample> ForClass(string cls, RulesPack rules)
        => Samples.GetOrAdd((rules, cls), key => new(() => FxShowcase.ForClass(key.Class, key.Rules))).Value;
}
