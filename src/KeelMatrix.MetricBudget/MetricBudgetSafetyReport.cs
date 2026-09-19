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
        long untrackedTagValueObservations,
        int maxTrackedInstrumentIdentities,
        int maxTrackedInstrumentInstances,
        int maxTrackedConflicts,
        int maxTrackedTagKeysPerInstrument,
        int maxTagCount,
        int maxInstrumentIdentityLength,
        int maxTagKeyLength,
        bool instrumentTrackingIncomplete,
        long untrackedInstrumentIdentities,
        long untrackedInstrumentInstances,
        bool conflictTrackingIncomplete,
        long untrackedConflicts,
        bool tagSetTrackingIncomplete,
        long untrackedTagSetObservations,
        bool tagKeyTrackingIncomplete,
        long untrackedTagKeyObservations)
    {
        MaxTrackedSeries = maxTrackedSeries;
        MaxTrackedValuesPerTag = maxTrackedValuesPerTag;
        MaxTagValueLength = maxTagValueLength;
        SeriesTrackingIncomplete = seriesTrackingIncomplete;
        UntrackedSeriesObservations = untrackedSeriesObservations;
        TagValueTrackingIncomplete = tagValueTrackingIncomplete;
        UntrackedTagValueObservations = untrackedTagValueObservations;
        MaxTrackedInstrumentIdentities = maxTrackedInstrumentIdentities;
        MaxTrackedInstrumentInstances = maxTrackedInstrumentInstances;
        MaxTrackedConflicts = maxTrackedConflicts;
        MaxTrackedTagKeysPerInstrument = maxTrackedTagKeysPerInstrument;
        MaxTagCount = maxTagCount;
        MaxInstrumentIdentityLength = maxInstrumentIdentityLength;
        MaxTagKeyLength = maxTagKeyLength;
        InstrumentTrackingIncomplete = instrumentTrackingIncomplete;
        UntrackedInstrumentIdentities = untrackedInstrumentIdentities;
        UntrackedInstrumentInstances = untrackedInstrumentInstances;
        ConflictTrackingIncomplete = conflictTrackingIncomplete;
        UntrackedConflicts = untrackedConflicts;
        TagSetTrackingIncomplete = tagSetTrackingIncomplete;
        UntrackedTagSetObservations = untrackedTagSetObservations;
        TagKeyTrackingIncomplete = tagKeyTrackingIncomplete;
        UntrackedTagKeyObservations = untrackedTagKeyObservations;
    }

    /// <summary>
    /// Configured bound on distinct observed series retained for one instrument identity.
    /// </summary>
    /// <remarks>
    /// The bound applies per instrument identity, so the series count a whole session can retain is this value
    /// multiplied by the number of matched instrument identities, plus the per-tag value sets.
    /// </remarks>
    public int MaxTrackedSeries { get; }

    /// <summary>
    /// Configured bound on distinct values retained per tag key and instrument identity.
    /// </summary>
    public int MaxTrackedValuesPerTag { get; }

    /// <summary>
    /// Configured bound on a tag value's invariant text before it is represented by a stable digest.
    /// </summary>
    public int MaxTagValueLength { get; }

    /// <summary>
    /// Whether the series safety bound for an instrument identity was reached, which makes every observed series
    /// count a lower bound.
    /// </summary>
    public bool SeriesTrackingIncomplete { get; }

    /// <summary>
    /// Measurements whose series could not be tracked because the series bound for an instrument identity was already
    /// reached.
    /// </summary>
    public long UntrackedSeriesObservations { get; }

    /// <summary>
    /// Whether the per-tag value safety bound for an instrument identity was reached for at least one tag key, which
    /// makes those observed distinct-value counts lower bounds.
    /// </summary>
    public bool TagValueTrackingIncomplete { get; }

    /// <summary>
    /// Measurements whose tag value could not be tracked because a per-tag bound was already reached.
    /// </summary>
    public long UntrackedTagValueObservations { get; }

    /// <summary>Configured bound on retained instrument identities.</summary>
    public int MaxTrackedInstrumentIdentities { get; }

    /// <summary>Configured bound on retained physical instrument instances.</summary>
    public int MaxTrackedInstrumentInstances { get; }

    /// <summary>Configured bound on retained ambiguous identity records.</summary>
    public int MaxTrackedConflicts { get; }

    /// <summary>Configured bound on retained tag keys per instrument identity.</summary>
    public int MaxTrackedTagKeysPerInstrument { get; }

    /// <summary>Configured bound on tags accepted from one measurement.</summary>
    public int MaxTagCount { get; }

    /// <summary>Configured bound on each meter and instrument identity component.</summary>
    public int MaxInstrumentIdentityLength { get; }

    /// <summary>Configured bound on a delivered tag key.</summary>
    public int MaxTagKeyLength { get; }

    /// <summary>Whether an instrument identity or physical instance bound was reached.</summary>
    public bool InstrumentTrackingIncomplete { get; }

    /// <summary>Selected published identities not retained after the identity bound was reached.</summary>
    public long UntrackedInstrumentIdentities { get; }

    /// <summary>Physical instrument instances not retained after the instance bound was reached.</summary>
    public long UntrackedInstrumentInstances { get; }

    /// <summary>Whether the conflict-record bound was reached.</summary>
    public bool ConflictTrackingIncomplete { get; }

    /// <summary>Ambiguous identities whose conflict record was not retained.</summary>
    public long UntrackedConflicts { get; }

    /// <summary>Whether one or more delivered tag sets could not be canonicalized.</summary>
    public bool TagSetTrackingIncomplete { get; }

    /// <summary>Measurements whose tag set could not be canonicalized.</summary>
    public long UntrackedTagSetObservations { get; }

    /// <summary>Whether the retained tag-key bound was reached.</summary>
    public bool TagKeyTrackingIncomplete { get; }

    /// <summary>Delivered tag keys that were not retained after the key bound was reached.</summary>
    public long UntrackedTagKeyObservations { get; }

    /// <summary>
    /// Whether all accounting completed inside the configured safety bounds.
    /// </summary>
    public bool IsComplete => !SeriesTrackingIncomplete
        && !TagValueTrackingIncomplete
        && !InstrumentTrackingIncomplete
        && !ConflictTrackingIncomplete
        && !TagSetTrackingIncomplete
        && !TagKeyTrackingIncomplete;
}
