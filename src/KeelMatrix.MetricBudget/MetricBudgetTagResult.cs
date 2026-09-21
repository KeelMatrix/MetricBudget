// Copyright (c) KeelMatrix

namespace KeelMatrix.MetricBudget;

/// <summary>
/// Observed distinct values for one tag key on one instrument, and the budget that applied to it.
/// </summary>
/// <remarks>
/// The result contains the tag key and counts only. Tag values are never included, so a report is safe to print in
/// shared logs and CI output even when tag values contain identifiers or other sensitive data.
/// <para>
/// <see cref="ObservedDistinctValueCount"/> is a lower bound whenever any tracking-completeness property is set.
/// A per-tag within-budget conclusion is therefore available only when all observation paths that could affect this
/// key were retained, including series tracking, tag-set admission, tag-key admission, instrument admission, and
/// per-key value tracking.
/// </para>
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
        long untrackedValueObservations,
        bool seriesTrackingIncomplete,
        bool tagSetTrackingIncomplete,
        bool tagKeyTrackingIncomplete,
        bool instrumentTrackingIncomplete)
    {
        Key = key;
        IsConfigured = isConfigured;
        WasObserved = wasObserved;
        ConfiguredMaxDistinctValues = configuredMaxDistinctValues;
        ObservedDistinctValueCount = observedDistinctValueCount;
        ValueTrackingIncomplete = valueTrackingIncomplete;
        UntrackedValueObservations = untrackedValueObservations;
        SeriesTrackingIncomplete = seriesTrackingIncomplete;
        TagSetTrackingIncomplete = tagSetTrackingIncomplete;
        TagKeyTrackingIncomplete = tagKeyTrackingIncomplete;
        InstrumentTrackingIncomplete = instrumentTrackingIncomplete;
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
    /// Distinct values observed for this key. A lower bound when any tracking-completeness property is
    /// <see langword="true"/>.
    /// </summary>
    public int ObservedDistinctValueCount { get; }

    /// <summary>
    /// Whether the per-tag value bound for this instrument identity was reached for this key, which means the
    /// observed count is a lower bound and no within-budget conclusion can be drawn from it.
    /// </summary>
    public bool ValueTrackingIncomplete { get; }

    /// <summary>
    /// Measurements whose tag value could not be tracked because the safety bound was already reached.
    /// </summary>
    public long UntrackedValueObservations { get; }

    /// <summary>
    /// Whether the instrument's series bound was reached. The observed value count may be incomplete because an
    /// untracked series could have carried this key.
    /// </summary>
    public bool SeriesTrackingIncomplete { get; }

    /// <summary>
    /// Whether one or more delivered tag sets containing this key could not be canonically tracked because they
    /// were oversized or contained an unsupported value.
    /// </summary>
    public bool TagSetTrackingIncomplete { get; }

    /// <summary>
    /// Whether the instrument's retained tag-key bound was reached. The observed value count may be incomplete
    /// because this key could have been among the keys that were not retained.
    /// </summary>
    public bool TagKeyTrackingIncomplete { get; }

    /// <summary>
    /// Whether selected physical instrument instances or same-name identities were not admitted, or a bounded
    /// name-only rejection index overflowed before the instrument was admitted. The observed value count may be
    /// incomplete because this key could have been delivered by an untracked source.
    /// </summary>
    public bool InstrumentTrackingIncomplete { get; }

    /// <summary>
    /// Whether this key and its instrument accounting stayed within budget and were tracked completely.
    /// </summary>
    public bool IsWithinBudget =>
        !SeriesTrackingIncomplete
        && !TagSetTrackingIncomplete
        && !TagKeyTrackingIncomplete
        && !InstrumentTrackingIncomplete
        && !ValueTrackingIncomplete
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
            + (IsWithinBudget ? string.Empty : ", tracking incomplete or over budget");
    }
}
