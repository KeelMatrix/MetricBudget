// Copyright (c) KeelMatrix

namespace KeelMatrix.MetricBudget;

/// <summary>
/// Safety bounds that applied to one session and whether any rejected additional observation or state.
/// </summary>
/// <remarks>
/// These bounds keep the verifier itself bounded under explosive cardinality. They are not budgets: rejecting
/// additional observation or state marks affected counts as lower bounds and normally moves the session outcome to
/// <see cref="MetricBudgetOutcome.ObservationIncomplete"/>. A selector conflict or proven budget breach has higher
/// outcome precedence, so inspect the completeness properties even when the top-level outcome differs.
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
        bool instrumentIdentityLengthTrackingIncomplete,
        long untrackedInstrumentIdentityLengths,
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
        InstrumentIdentityLengthTrackingIncomplete = instrumentIdentityLengthTrackingIncomplete;
        UntrackedInstrumentIdentityLengths = untrackedInstrumentIdentityLengths;
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
    /// Whether an additional distinct series could not be retained after the series safety bound for an instrument
    /// identity was full, which makes the observed series count a lower bound.
    /// </summary>
    public bool SeriesTrackingIncomplete { get; }

    /// <summary>
    /// Measurements whose new series could not be tracked because the series bound for an instrument identity was
    /// already full.
    /// </summary>
    public long UntrackedSeriesObservations { get; }

    /// <summary>
    /// Whether an additional distinct value could not be retained after the per-tag value safety bound for an
    /// instrument identity was full for at least one tag key, which makes those observed distinct-value counts lower
    /// bounds.
    /// </summary>
    public bool TagValueTrackingIncomplete { get; }

    /// <summary>
    /// Tag-value occurrences whose distinct value could not be tracked because a per-tag bound was already full.
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

    /// <summary>
    /// Whether a selected identity, physical instance, or identity-length bound rejected state, or a bounded
    /// name-only rejection index overflowed before a later identity was admitted.
    /// </summary>
    public bool InstrumentTrackingIncomplete { get; }

    /// <summary>Selected published identities not retained after the identity bound was full.</summary>
    public long UntrackedInstrumentIdentities { get; }

    /// <summary>Physical instrument instances not retained after the instance bound was full.</summary>
    public long UntrackedInstrumentInstances { get; }

    /// <summary>
    /// Whether selected instrument identities were rejected because a meter name, meter version, or instrument
    /// name exceeded <see cref="MaxInstrumentIdentityLength"/>.
    /// </summary>
    public bool InstrumentIdentityLengthTrackingIncomplete { get; }

    /// <summary>
    /// Selected published identities rejected by the instrument identity component-length bound.
    /// </summary>
    public long UntrackedInstrumentIdentityLengths { get; }

    /// <summary>Whether the conflict-record bound was full and rejected an additional conflict record.</summary>
    public bool ConflictTrackingIncomplete { get; }

    /// <summary>Ambiguous identities whose conflict record was not retained.</summary>
    public long UntrackedConflicts { get; }

    /// <summary>Whether one or more delivered tag sets could not be canonicalized.</summary>
    public bool TagSetTrackingIncomplete { get; }

    /// <summary>Measurements whose tag set could not be canonicalized.</summary>
    public long UntrackedTagSetObservations { get; }

    /// <summary>Whether the retained tag-key bound was full and rejected an additional key.</summary>
    public bool TagKeyTrackingIncomplete { get; }

    /// <summary>Delivered tag keys that were not retained after the key bound was full.</summary>
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
