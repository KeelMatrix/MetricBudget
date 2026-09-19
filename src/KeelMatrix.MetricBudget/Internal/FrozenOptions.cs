// Copyright (c) KeelMatrix

namespace KeelMatrix.MetricBudget.Internal;

/// <summary>
/// Immutable configuration a session keeps for its whole lifetime.
/// </summary>
/// <remarks>
/// Copying the configuration at start time keeps a running session independent of later changes to the options
/// object, and lets measurement callbacks read limits without locking.
/// </remarks>
internal sealed class FrozenOptions
{
    internal FrozenOptions(
        int maxTrackedSeries,
        int maxTrackedValuesPerTag,
        int maxTagValueLength,
        int maxTrackedInstrumentIdentities,
        int maxTrackedInstrumentInstances,
        int maxTrackedConflicts,
        int maxTrackedTagKeysPerInstrument,
        int maxTagCount,
        int maxInstrumentIdentityLength,
        int maxTagKeyLength,
        FrozenRule[] rules)
    {
        MaxTrackedSeries = maxTrackedSeries;
        MaxTrackedValuesPerTag = maxTrackedValuesPerTag;
        MaxTagValueLength = maxTagValueLength;
        MaxTrackedInstrumentIdentities = maxTrackedInstrumentIdentities;
        MaxTrackedInstrumentInstances = maxTrackedInstrumentInstances;
        MaxTrackedConflicts = maxTrackedConflicts;
        MaxTrackedTagKeysPerInstrument = maxTrackedTagKeysPerInstrument;
        MaxTagCount = maxTagCount;
        MaxInstrumentIdentityLength = maxInstrumentIdentityLength;
        MaxTagKeyLength = maxTagKeyLength;
        Rules = rules;
    }

    internal int MaxTrackedSeries { get; }

    internal int MaxTrackedValuesPerTag { get; }

    internal int MaxTagValueLength { get; }

    internal int MaxTrackedInstrumentIdentities { get; }

    internal int MaxTrackedInstrumentInstances { get; }

    internal int MaxTrackedConflicts { get; }

    internal int MaxTrackedTagKeysPerInstrument { get; }

    internal int MaxTagCount { get; }

    internal int MaxInstrumentIdentityLength { get; }

    internal int MaxTagKeyLength { get; }

    internal FrozenRule[] Rules { get; }
}

/// <summary>
/// Immutable instrument-selection rule and budget.
/// </summary>
internal sealed class FrozenRule
{
    private readonly Dictionary<string, int> tagBudgets;

    private FrozenRule(
        InstrumentSelector selector,
        int? maxObservedSeries,
        TagBudgetLimit[] orderedTagBudgets,
        Dictionary<string, int> tagBudgets)
    {
        Selector = selector;
        MaxObservedSeries = maxObservedSeries;
        OrderedTagBudgets = orderedTagBudgets;
        this.tagBudgets = tagBudgets;
    }

    internal InstrumentSelector Selector { get; }

    internal int? MaxObservedSeries { get; }

    internal TagBudgetLimit[] OrderedTagBudgets { get; }

    internal bool Matches(in InstrumentIdentity identity)
    {
        return Selector.Matches(identity);
    }

    internal bool TryGetTagBudget(string tagKey, out int maxDistinctValues)
    {
        return tagBudgets.TryGetValue(tagKey, out maxDistinctValues);
    }

    internal static FrozenRule Create(InstrumentBudget budget, InstrumentSelector selector)
    {
        IReadOnlyList<TagBudget> tags = budget.Tags;
        TagBudgetLimit[] ordered = new TagBudgetLimit[tags.Count];
        Dictionary<string, int> byKey = new Dictionary<string, int>(tags.Count, StringComparer.Ordinal);

        for (int i = 0; i < tags.Count; i++)
        {
            TagBudget tag = tags[i];
            int limit = tag.MaxDistinctValues ?? 0;
            ordered[i] = new TagBudgetLimit(tag.Key, limit);
            byKey[tag.Key] = limit;
        }

        return new FrozenRule(selector, budget.MaxObservedSeries, ordered, byKey);
    }
}

/// <summary>
/// One declared tag budget in declaration order.
/// </summary>
internal readonly struct TagBudgetLimit
{
    internal TagBudgetLimit(string key, int maxDistinctValues)
    {
        Key = key;
        MaxDistinctValues = maxDistinctValues;
    }

    internal string Key { get; }

    internal int MaxDistinctValues { get; }
}
