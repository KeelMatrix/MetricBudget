// Copyright (c) KeelMatrix

using System.Globalization;

namespace KeelMatrix.MetricBudget;

/// <summary>
/// Declares the observed-cardinality budget for one instrument selection.
/// </summary>
/// <remarks>
/// <para>
/// The budget constrains what the exercised workload actually produced: the number of distinct observed series for
/// the instrument, and the number of distinct values each configured tag key may produce. It never claims to bound
/// the cardinality production can produce.
/// </para>
/// <para>
/// A rule must configure <see cref="MaxObservedSeries"/> or at least one tag budget. A budget with no limit is
/// invalid configuration rather than an implicit default, because no default limit is safe for every codebase.
/// </para>
/// </remarks>
public sealed class InstrumentBudget
{
    private readonly Dictionary<string, TagBudget> tagsByKey = new(StringComparer.Ordinal);
    private readonly List<TagBudget> orderedTags = new();

    /// <summary>
    /// Maximum number of distinct observed series the instrument may produce within the session.
    /// </summary>
    /// <remarks>
    /// One observed series is one combination of instrument identity and tag set, so repeated measurements of the
    /// same combination stay one series. The value must be greater than zero, and <see langword="null"/> means no
    /// series budget is configured for this rule.
    /// </remarks>
    public int? MaxObservedSeries { get; set; }

    /// <summary>
    /// Tag budgets declared for this rule, in declaration order.
    /// </summary>
    public IReadOnlyList<TagBudget> Tags => orderedTags;

    /// <summary>
    /// Declares or returns the budget for one tag key.
    /// </summary>
    /// <param name="tagKey">Exact delivered tag key.</param>
    /// <returns>The budget for <paramref name="tagKey"/>; repeated calls return the same instance.</returns>
    /// <exception cref="ArgumentException"><paramref name="tagKey"/> is null or empty.</exception>
    public TagBudget Tag(string tagKey)
    {
        if (string.IsNullOrEmpty(tagKey))
        {
            throw new ArgumentException("A tag key must not be null or empty.", nameof(tagKey));
        }

        if (!tagsByKey.TryGetValue(tagKey, out TagBudget? budget))
        {
            budget = new TagBudget(tagKey);
            tagsByKey.Add(tagKey, budget);
            orderedTags.Add(budget);
        }

        return budget;
    }

    /// <summary>
    /// Returns whether this budget configures any limit at all.
    /// </summary>
    /// <returns><see langword="true"/> when a series budget or at least one tag budget is set.</returns>
    public bool HasLimit()
    {
        if (MaxObservedSeries.HasValue)
        {
            return true;
        }

        foreach (TagBudget tag in orderedTags)
        {
            if (tag.MaxDistinctValues.HasValue)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Describes the configured budget.
    /// </summary>
    /// <returns>A short description.</returns>
    public override string ToString()
    {
        List<string> parts = new List<string>();
        if (MaxObservedSeries.HasValue)
        {
            parts.Add("observed series <= " + MaxObservedSeries.Value.ToString(CultureInfo.InvariantCulture));
        }

        foreach (TagBudget tag in orderedTags)
        {
            parts.Add(tag.ToString());
        }

        return parts.Count == 0 ? "<no budget configured>" : string.Join("; ", parts);
    }

}
