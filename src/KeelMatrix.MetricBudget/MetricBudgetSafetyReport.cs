// Copyright (c) KeelMatrix

namespace KeelMatrix.MetricBudget;

/// <summary>
/// Safety bounds that applied to one session and whether any of them was reached.
/// </summary>
/// <remarks>
/// These bounds keep the verifier itself bounded under explosive cardinality. They are not budgets: reaching one
/// never turns a failing workload into a passing report, it only marks the observed counts as lower bounds and
/// moves the session outcome to <see cref="MetricBudgetOutcome.ObservationIncomplete"/>.
/// </remarks>
public sealed class MetricBudgetSafetyReport
{
    internal MetricBudgetSafetyReport(
        int maxTrackedSeries,
        int maxTrackedValuesPerTag,
        int maxTagValueLength,
        bool seriesTrackingIncomplete,
        long untrackedSeriesObservations,
        bool tagValueTrackingIncomplete,
        long untrackedTagValueObservations)
    {
        MaxTrackedSeries = maxTrackedSeries;
        MaxTrackedValuesPerTag = maxTrackedValuesPerTag;
        MaxTagValueLength = maxTagValueLength;
        SeriesTrackingIncomplete = seriesTrackingIncomplete;
        UntrackedSeriesObservations = untrackedSeriesObservations;
        TagValueTrackingIncomplete = tagValueTrackingIncomplete;
        UntrackedTagValueObservations = untrackedTagValueObservations;
    }

    /// <summary>
    /// Configured bound on distinct observed series retained across the session.
    /// </summary>
    public int MaxTrackedSeries { get; }

    /// <summary>
    /// Configured bound on distinct values retained per tag key and instrument.
    /// </summary>
    public int MaxTrackedValuesPerTag { get; }

    /// <summary>
    /// Configured bound on a tag value's invariant text before it is represented by a stable digest.
    /// </summary>
    public int MaxTagValueLength { get; }

    /// <summary>
    /// Whether the series safety bound was reached, which makes every observed series count a lower bound.
    /// </summary>
    public bool SeriesTrackingIncomplete { get; }

    /// <summary>
    /// Measurements whose series could not be tracked because the series bound was already reached.
    /// </summary>
    public long UntrackedSeriesObservations { get; }

    /// <summary>
    /// Whether the per-tag value safety bound was reached for at least one tag key, which makes those observed
    /// distinct-value counts lower bounds.
    /// </summary>
    public bool TagValueTrackingIncomplete { get; }

    /// <summary>
    /// Measurements whose tag value could not be tracked because a per-tag bound was already reached.
    /// </summary>
    public long UntrackedTagValueObservations { get; }

    /// <summary>
    /// Whether all accounting completed inside the configured safety bounds.
    /// </summary>
    public bool IsComplete => !SeriesTrackingIncomplete && !TagValueTrackingIncomplete;
}
