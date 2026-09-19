// Copyright (c) KeelMatrix

using System.Collections.ObjectModel;
using System.Globalization;

namespace KeelMatrix.MetricBudget;

/// <summary>
/// Immutable description of the budget captured by a completed report rule.
/// </summary>
/// <remarks>
/// This type is separate from the mutable configuration object used by
/// <see cref="InstrumentBudget"/>. A completed report
/// never exposes a mutable budget graph.
/// </remarks>
public sealed class MetricBudgetBudget
{
    private MetricBudgetBudget(int? maxObservedSeries, IReadOnlyList<MetricBudgetTagBudget> tags)
    {
        MaxObservedSeries = maxObservedSeries;
        MetricBudgetTagBudget[] copy = new MetricBudgetTagBudget[tags.Count];
        for (int i = 0; i < copy.Length; i++)
        {
            copy[i] = tags[i];
        }

        Tags = new ReadOnlyCollection<MetricBudgetTagBudget>(copy);
    }

    /// <summary>Maximum observed series configured for the rule, or <see langword="null"/>.</summary>
    public int? MaxObservedSeries { get; }

    /// <summary>Immutable tag budgets in declaration order.</summary>
    public IReadOnlyList<MetricBudgetTagBudget> Tags { get; }

    internal static MetricBudgetBudget From(InstrumentBudget budget)
    {
        MetricBudgetTagBudget[] tags = new MetricBudgetTagBudget[budget.Tags.Count];
        for (int i = 0; i < tags.Length; i++)
        {
            TagBudget tag = budget.Tags[i];
            tags[i] = new MetricBudgetTagBudget(tag.Key, tag.MaxDistinctValues);
        }

        return new MetricBudgetBudget(budget.MaxObservedSeries, tags);
    }

    internal static MetricBudgetBudget Create(int? maxObservedSeries, IReadOnlyList<MetricBudgetTagBudget> tags)
    {
        return new MetricBudgetBudget(maxObservedSeries, tags);
    }

    /// <summary>Describes the configured limits.</summary>
    public override string ToString()
    {
        List<string> parts = new List<string>();
        if (MaxObservedSeries.HasValue)
        {
            parts.Add("observed series <= " + MaxObservedSeries.Value.ToString(CultureInfo.InvariantCulture));
        }

        for (int i = 0; i < Tags.Count; i++)
        {
            parts.Add(Tags[i].ToString());
        }

        return parts.Count == 0 ? "<no budget configured>" : string.Join("; ", parts);
    }
}

/// <summary>One immutable tag-budget description in a completed report.</summary>
public sealed class MetricBudgetTagBudget
{
    internal MetricBudgetTagBudget(string key, int? maxDistinctValues)
    {
        Key = key;
        MaxDistinctValues = maxDistinctValues;
    }

    /// <summary>The exact configured tag key.</summary>
    public string Key { get; }

    /// <summary>Maximum distinct values configured for the tag, or <see langword="null"/>.</summary>
    public int? MaxDistinctValues { get; }

    /// <summary>Describes the configured tag limit.</summary>
    public override string ToString()
    {
        return "\"" + Key + "\" <= "
            + (MaxDistinctValues.HasValue
                ? MaxDistinctValues.Value.ToString(CultureInfo.InvariantCulture)
                : "<unset>")
            + " distinct values";
    }
}
