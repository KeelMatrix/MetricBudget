// Copyright (c) KeelMatrix

using System.Globalization;

namespace KeelMatrix.MetricBudget;

/// <summary>
/// Observed-cardinality accounting for one instrument identity within a session.
/// </summary>
/// <remarks>
/// One result covers one combination of meter name, meter version, instrument name, and instrument kind. Counts
/// describe the exercised workload only.
/// </remarks>
public sealed class MetricBudgetInstrumentResult
{
    internal MetricBudgetInstrumentResult(
        string meterName,
        string? meterVersion,
        string instrumentName,
        MetricInstrumentKind instrumentKind,
        long measurementCount,
        int observedSeriesCount,
        int? configuredMaxObservedSeries,
        long untrackedSeriesObservations,
        bool seriesTrackingIncomplete,
        IReadOnlyList<MetricBudgetTagResult> tags)
    {
        MeterName = meterName;
        MeterVersion = meterVersion;
        InstrumentName = instrumentName;
        InstrumentKind = instrumentKind;
        MeasurementCount = measurementCount;
        ObservedSeriesCount = observedSeriesCount;
        ConfiguredMaxObservedSeries = configuredMaxObservedSeries;
        UntrackedSeriesObservations = untrackedSeriesObservations;
        SeriesTrackingIncomplete = seriesTrackingIncomplete;
        Tags = tags;
    }

    /// <summary>
    /// Meter name of the observed instrument identity.
    /// </summary>
    public string MeterName { get; }

    /// <summary>
    /// Meter version of the observed instrument identity, or <see langword="null"/> when the meter declares none.
    /// </summary>
    public string? MeterVersion { get; }

    /// <summary>
    /// Instrument name of the observed instrument identity.
    /// </summary>
    public string InstrumentName { get; }

    /// <summary>
    /// Kind of the observed instrument.
    /// </summary>
    public MetricInstrumentKind InstrumentKind { get; }

    /// <summary>
    /// Measurements delivered by this instrument identity during the session.
    /// </summary>
    public long MeasurementCount { get; }

    /// <summary>
    /// Distinct observed series tracked for this instrument identity. A lower bound when
    /// <see cref="SeriesTrackingIncomplete"/> is <see langword="true"/>.
    /// </summary>
    public int ObservedSeriesCount { get; }

    /// <summary>
    /// Configured maximum observed series for this instrument identity, or <see langword="null"/> when the rule declares no
    /// per-instrument-identity series budget.
    /// </summary>
    public int? ConfiguredMaxObservedSeries { get; }

    /// <summary>
    /// Measurements whose series could not be tracked because the series safety bound for this instrument identity
    /// was already reached.
    /// </summary>
    public long UntrackedSeriesObservations { get; }

    /// <summary>
    /// Whether the series safety bound for this instrument identity was reached, which means
    /// <see cref="ObservedSeriesCount"/> is a lower bound.
    /// </summary>
    public bool SeriesTrackingIncomplete { get; }

    /// <summary>
    /// Per-tag results for this instrument identity, ordered by tag key, including configured keys the workload
    /// never delivered.
    /// </summary>
    public IReadOnlyList<MetricBudgetTagResult> Tags { get; }

    /// <summary>
    /// Whether this instrument delivered at least one measurement.
    /// </summary>
    public bool WasObserved => MeasurementCount > 0;

    /// <summary>
    /// Whether this instrument stayed within its configured budgets and was tracked completely.
    /// </summary>
    public bool IsWithinBudget
    {
        get
        {
            if (!WasObserved || SeriesTrackingIncomplete)
            {
                return false;
            }

            if (ConfiguredMaxObservedSeries.HasValue && ObservedSeriesCount > ConfiguredMaxObservedSeries.Value)
            {
                return false;
            }

            for (int i = 0; i < Tags.Count; i++)
            {
                if (!Tags[i].IsWithinBudget)
                {
                    return false;
                }
            }

            return true;
        }
    }

    /// <summary>
    /// Describes the observed series count, the configured limit, and the largest tag fan-out.
    /// </summary>
    /// <returns>A short description.</returns>
    public override string ToString()
    {
        string limit = ConfiguredMaxObservedSeries.HasValue
            ? ConfiguredMaxObservedSeries.Value.ToString(CultureInfo.InvariantCulture)
            : "none";

        return MeterName
            + (MeterVersion is null ? string.Empty : "@" + MeterVersion)
            + "/" + InstrumentName
            + " (" + InstrumentKind.ToString().ToLowerInvariant() + ")"
            + ": " + MeasurementCount.ToString(CultureInfo.InvariantCulture) + " measurements, "
            + ObservedSeriesCount.ToString(CultureInfo.InvariantCulture) + " observed series, configured max "
            + limit
            + (SeriesTrackingIncomplete ? ", tracking incomplete" : string.Empty);
    }
}
