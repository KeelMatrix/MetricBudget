// Copyright (c) KeelMatrix

namespace KeelMatrix.MetricBudget;

/// <summary>
/// Observed distinct values for one tag key on one instrument, and the budget that applied to it.
/// </summary>
/// <remarks>
/// The result contains the tag key and counts only. Tag values are never included, so a report is safe to print in
/// shared logs and CI output even when tag values contain identifiers or other sensitive data.
/// </remarks>
public sealed class MetricBudgetTagResult
{
    internal MetricBudgetTagResult(
        string? key,
        bool isConfigured,
        bool wasObserved,
        int? configuredMaxDistinctValues,
        int observedDistinctValueCount,
        bool valueTrackingIncomplete,
        long untrackedValueObservations)
    {
        Key = key;
        IsConfigured = isConfigured;
        WasObserved = wasObserved;
        ConfiguredMaxDistinctValues = configuredMaxDistinctValues;
        ObservedDistinctValueCount = observedDistinctValueCount;
        ValueTrackingIncomplete = valueTrackingIncomplete;
        UntrackedValueObservations = untrackedValueObservations;
    }

    /// <summary>
    /// The delivered tag key, or <see langword="null"/> when the workload delivered a <see langword="null"/> tag
    /// key.
    /// </summary>
    public string? Key { get; }

    /// <summary>
    /// Whether a tag budget was declared for this key.
    /// </summary>
    public bool IsConfigured { get; }

    /// <summary>
    /// Whether the workload delivered this tag key on the instrument during the session.
    /// </summary>
    public bool WasObserved { get; }

    /// <summary>
    /// Configured maximum distinct values for this key, or <see langword="null"/> when no budget applies.
    /// </summary>
    public int? ConfiguredMaxDistinctValues { get; }

    /// <summary>
    /// Distinct values observed for this key. A lower bound when <see cref="ValueTrackingIncomplete"/> is
    /// <see langword="true"/>.
    /// </summary>
    public int ObservedDistinctValueCount { get; }

    /// <summary>
    /// Whether the session reached its per-tag value bound for this key, which means the observed count is a lower
    /// bound and no within-budget conclusion can be drawn from it.
    /// </summary>
    public bool ValueTrackingIncomplete { get; }

    /// <summary>
    /// Measurements whose tag value could not be tracked because the safety bound was already reached.
    /// </summary>
    public long UntrackedValueObservations { get; }

    /// <summary>
    /// Whether this key stayed within its budget and was tracked completely.
    /// </summary>
    public bool IsWithinBudget =>
        !ValueTrackingIncomplete
        && (!ConfiguredMaxDistinctValues.HasValue || ObservedDistinctValueCount <= ConfiguredMaxDistinctValues.Value);

    /// <summary>
    /// Describes the observed count, the configured limit, and whether tracking was complete.
    /// </summary>
    /// <returns>A short description.</returns>
    public override string ToString()
    {
        string key = Key is null ? "<null tag key>" : "\"" + Key + "\"";
        string limit = ConfiguredMaxDistinctValues.HasValue
            ? ConfiguredMaxDistinctValues.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)
            : "none";

        return key
            + ": " + ObservedDistinctValueCount.ToString(System.Globalization.CultureInfo.InvariantCulture)
            + " observed distinct values, configured max " + limit
            + (ValueTrackingIncomplete ? ", tracking incomplete" : string.Empty);
    }
}
