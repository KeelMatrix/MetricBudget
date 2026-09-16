// Copyright (c) KeelMatrix

namespace KeelMatrix.MetricBudget.Internal;

/// <summary>
/// Consistent point-in-time view of one completed session.
/// </summary>
/// <remarks>
/// Every member is copied while the session writer lock is held, so a snapshot never mixes values from different
/// moments and cannot report an impossible state such as more distinct observed series than measurements.
/// </remarks>
internal sealed class SessionSnapshot
{
    internal SessionSnapshot(
        InstrumentAccountSnapshot[] instruments,
        ConfigurationConflictSnapshot[] conflicts,
        long measurementsDelivered,
        long unmatchedMeasurements,
        int maxTrackedSeries,
        int maxTrackedValuesPerTag,
        int maxTagValueLength)
    {
        Instruments = instruments;
        Conflicts = conflicts;
        MeasurementsDelivered = measurementsDelivered;
        UnmatchedMeasurements = unmatchedMeasurements;
        MaxTrackedSeries = maxTrackedSeries;
        MaxTrackedValuesPerTag = maxTrackedValuesPerTag;
        MaxTagValueLength = maxTagValueLength;
    }

    internal InstrumentAccountSnapshot[] Instruments { get; }

    internal ConfigurationConflictSnapshot[] Conflicts { get; }

    /// <summary>Measurement callbacks delivered to the session.</summary>
    internal long MeasurementsDelivered { get; }

    /// <summary>Delivery callbacks that could not be attributed to a selected instrument.</summary>
    internal long UnmatchedMeasurements { get; }

    internal int MaxTrackedSeries { get; }

    internal int MaxTrackedValuesPerTag { get; }

    internal int MaxTagValueLength { get; }

    internal long AccountedMeasurements
    {
        get
        {
            long total = 0;
            for (int i = 0; i < Instruments.Length; i++)
            {
                total += Instruments[i].MeasurementCount;
            }

            return total;
        }
    }

    internal bool SeriesTrackingIncomplete
    {
        get
        {
            for (int i = 0; i < Instruments.Length; i++)
            {
                if (Instruments[i].SeriesTrackingIncomplete)
                {
                    return true;
                }
            }

            return false;
        }
    }

    internal bool TagValueTrackingIncomplete
    {
        get
        {
            for (int i = 0; i < Instruments.Length; i++)
            {
                if (Instruments[i].TagValueTrackingIncomplete)
                {
                    return true;
                }
            }

            return false;
        }
    }
}

/// <summary>
/// Consistent point-in-time view of one instrument identity.
/// </summary>
internal sealed class InstrumentAccountSnapshot
{
    internal InstrumentAccountSnapshot(
        InstrumentIdentity identity,
        int ruleIndex,
        long measurementCount,
        long newSeriesObservations,
        long existingSeriesObservations,
        long untrackedSeriesObservations,
        int observedSeriesCount,
        bool seriesCapExhausted,
        bool tagValueCapExhausted,
        TagValueSnapshot[] tags)
    {
        Identity = identity;
        RuleIndex = ruleIndex;
        MeasurementCount = measurementCount;
        NewSeriesObservations = newSeriesObservations;
        ExistingSeriesObservations = existingSeriesObservations;
        UntrackedSeriesObservations = untrackedSeriesObservations;
        ObservedSeriesCount = observedSeriesCount;
        SeriesCapExhausted = seriesCapExhausted;
        TagValueCapExhausted = tagValueCapExhausted;
        Tags = tags;
    }

    internal InstrumentIdentity Identity { get; }

    internal int RuleIndex { get; }

    internal long MeasurementCount { get; }

    internal long NewSeriesObservations { get; }

    internal long ExistingSeriesObservations { get; }

    internal long UntrackedSeriesObservations { get; }

    /// <summary>
    /// Distinct observed series that are tracked in full. A lower bound when
    /// <see cref="SeriesTrackingIncomplete"/> is <see langword="true"/>.
    /// </summary>
    internal int ObservedSeriesCount { get; }

    internal bool SeriesCapExhausted { get; }

    internal bool TagValueCapExhausted { get; }

    internal TagValueSnapshot[] Tags { get; }

    internal bool SeriesTrackingIncomplete => SeriesCapExhausted || UntrackedSeriesObservations > 0;

    internal bool TagValueTrackingIncomplete
    {
        get
        {
            if (TagValueCapExhausted)
            {
                return true;
            }

            for (int i = 0; i < Tags.Length; i++)
            {
                if (Tags[i].ValueTrackingIncomplete)
                {
                    return true;
                }
            }

            return false;
        }
    }
}

/// <summary>
/// Consistent point-in-time view of one encoded tag key within one instrument.
/// </summary>
internal sealed class TagValueSnapshot
{
    internal TagValueSnapshot(
        string keyField,
        int observedDistinctValueCount,
        bool valueTrackingIncomplete,
        long untrackedValueObservations)
    {
        KeyField = keyField;
        ObservedDistinctValueCount = observedDistinctValueCount;
        ValueTrackingIncomplete = valueTrackingIncomplete;
        UntrackedValueObservations = untrackedValueObservations;
    }

    internal string KeyField { get; }

    /// <summary>
    /// Distinct values tracked for this tag key. A lower bound when <see cref="ValueTrackingIncomplete"/> is
    /// <see langword="true"/>.
    /// </summary>
    internal int ObservedDistinctValueCount { get; }

    internal bool ValueTrackingIncomplete { get; }

    internal long UntrackedValueObservations { get; }
}

/// <summary>
/// One instrument identity that matched more than one configured rule.
/// </summary>
internal sealed class ConfigurationConflictSnapshot
{
    internal ConfigurationConflictSnapshot(InstrumentIdentity identity, int[] ruleIndexes)
    {
        Identity = identity;
        RuleIndexes = ruleIndexes;
    }

    internal InstrumentIdentity Identity { get; }

    internal int[] RuleIndexes { get; }
}
