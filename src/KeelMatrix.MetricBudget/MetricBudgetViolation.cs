// Copyright (c) KeelMatrix

namespace KeelMatrix.MetricBudget;

/// <summary>
/// What kind of problem one structured record describes.
/// </summary>
public enum MetricBudgetViolationKind
{
    /// <summary>One instrument identity produced more distinct observed series than its configured budget.</summary>
    ObservedSeriesBudgetExceeded = 0,

    /// <summary>One tag key on one instrument identity produced more distinct values than its configured budget.</summary>
    TagDistinctValuesBudgetExceeded = 1,

    /// <summary>At least one configured rule selected no published instrument.</summary>
    InstrumentNotObserved = 2,

    /// <summary>A selected instrument was published but delivered no measurements.</summary>
    InstrumentProducedNoMeasurements = 3,

    /// <summary>A safety bound rejected additional observation or state, so affected observed counts are lower bounds.</summary>
    SafetyLimitReached = 4,

    /// <summary>One instrument identity matched more than one rule, so no budget can be applied.</summary>
    ConfigurationInvalid = 5,

    /// <summary>
    /// The session detected accounting that cannot be true, for example tracked series outnumbering measurements.
    /// This should never happen and is reported loudly rather than ignored.
    /// </summary>
    AccountingInconsistent = 6,
}

/// <summary>
/// One structured problem record from a completed verification.
/// </summary>
/// <remarks>
/// A record carries instrument identity, limits, counts, and tag keys. It never carries a raw tag value, a metric
/// value, or a sample of the observed workload.
/// </remarks>
public sealed class MetricBudgetViolation
{
    internal MetricBudgetViolation(
        MetricBudgetViolationKind kind,
        string description,
        string? meterName,
        string? meterVersion,
        string? instrumentName,
        MetricInstrumentKind instrumentKind,
        string? tagKey,
        int? observedCount,
        int? configuredLimit)
    {
        Kind = kind;
        Description = description;
        MeterName = meterName;
        MeterVersion = meterVersion;
        InstrumentName = instrumentName;
        InstrumentKind = instrumentKind;
        TagKey = tagKey;
        ObservedCount = observedCount;
        ConfiguredLimit = configuredLimit;
    }

    /// <summary>
    /// Kind of problem this record describes.
    /// </summary>
    public MetricBudgetViolationKind Kind { get; }

    /// <summary>
    /// Actionable description of the problem. It excludes tag values and metric values but can contain application-
    /// supplied identity and tag keys.
    /// </summary>
    public string Description { get; }

    /// <summary>
    /// Meter name when the record refers to an instrument identity, otherwise <see langword="null"/>.
    /// </summary>
    public string? MeterName { get; }

    /// <summary>
    /// Meter version when the meter declares one, otherwise <see langword="null"/>.
    /// </summary>
    public string? MeterVersion { get; }

    /// <summary>
    /// Instrument name when the record refers to an instrument identity, otherwise <see langword="null"/>.
    /// </summary>
    public string? InstrumentName { get; }

    /// <summary>
    /// Instrument kind when the record refers to an instrument identity, otherwise
    /// <see cref="MetricInstrumentKind.Unknown"/>.
    /// </summary>
    public MetricInstrumentKind InstrumentKind { get; }

    /// <summary>
    /// Tag key for tag-level records, otherwise <see langword="null"/>. A delivered <see langword="null"/> tag key
    /// is reported as <see langword="null"/>.
    /// </summary>
    public string? TagKey { get; }

    /// <summary>
    /// Observed count for the record, or <see langword="null"/> when the record is not a count comparison.
    /// </summary>
    public int? ObservedCount { get; }

    /// <summary>
    /// Configured limit for the record, or <see langword="null"/> when no limit applies.
    /// </summary>
    public int? ConfiguredLimit { get; }

    /// <summary>
    /// Returns the actionable description.
    /// </summary>
    /// <returns>The description of this record.</returns>
    public override string ToString()
    {
        return Description;
    }
}
