using System.Globalization;

namespace MetricBudget.Probe;

internal enum ProbeRecordOutcome
{
    NewSeries,
    ExistingSeries,
    Exhausted,
}

/// <summary>
/// Consistent point-in-time view of one tracker's accounting.
/// </summary>
/// <remarks>
/// Every member is copied while the tracker's writer lock is held, so a snapshot never mixes values from
/// different moments and can never report an impossible state such as more tracked series than observed
/// measurements.
/// </remarks>
internal readonly struct ProbeTrackerSnapshot
{
    public ProbeTrackerSnapshot(
        long observedMeasurements,
        long newSeriesObservations,
        long existingSeriesObservations,
        long untrackedSeriesObservations,
        int trackedSeriesCount,
        int trackedTagKeys,
        int trackedTagValues,
        int seriesCap,
        int tagValueCapPerKey,
        int tagKeyCap,
        bool seriesCapExhausted,
        bool tagValueCapExhausted,
        bool tagKeyCapExhausted,
        string? sampleExhaustedSeriesKey)
    {
        ObservedMeasurements = observedMeasurements;
        NewSeriesObservations = newSeriesObservations;
        ExistingSeriesObservations = existingSeriesObservations;
        UntrackedSeriesObservations = untrackedSeriesObservations;
        TrackedSeriesCount = trackedSeriesCount;
        TrackedTagKeys = trackedTagKeys;
        TrackedTagValues = trackedTagValues;
        SeriesCap = seriesCap;
        TagValueCapPerKey = tagValueCapPerKey;
        TagKeyCap = tagKeyCap;
        SeriesCapExhausted = seriesCapExhausted;
        TagValueCapExhausted = tagValueCapExhausted;
        TagKeyCapExhausted = tagKeyCapExhausted;
        SampleExhaustedSeriesKey = sampleExhaustedSeriesKey;
    }

    public long ObservedMeasurements { get; }

    public long NewSeriesObservations { get; }

    public long ExistingSeriesObservations { get; }

    public long UntrackedSeriesObservations { get; }

    public int TrackedSeriesCount { get; }

    public int TrackedTagKeys { get; }

    public int TrackedTagValues { get; }

    public int SeriesCap { get; }

    public int TagValueCapPerKey { get; }

    public int TagKeyCap { get; }

    public bool SeriesCapExhausted { get; }

    public bool TagValueCapExhausted { get; }

    public bool TagKeyCapExhausted { get; }

    public string? SampleExhaustedSeriesKey { get; }

    public bool IsIncomplete => SeriesCapExhausted || TagValueCapExhausted || TagKeyCapExhausted;

    /// <summary>
    /// Every observation is classified exactly once, and distinct tracked series can never outnumber observed
    /// measurements.
    /// </summary>
    public bool AccountingIsConsistent =>
        NewSeriesObservations + ExistingSeriesObservations + UntrackedSeriesObservations == ObservedMeasurements
        && TrackedSeriesCount <= ObservedMeasurements;

    public string Describe()
    {
        return "observed=" + ObservedMeasurements.ToString(CultureInfo.InvariantCulture)
            + "; newSeriesObservations=" + NewSeriesObservations.ToString(CultureInfo.InvariantCulture)
            + "; existingSeriesObservations=" + ExistingSeriesObservations.ToString(CultureInfo.InvariantCulture)
            + "; trackedSeries=" + TrackedSeriesCount.ToString(CultureInfo.InvariantCulture)
            + "/" + SeriesCap.ToString(CultureInfo.InvariantCulture)
            + "; untrackedSeriesObservations=" + UntrackedSeriesObservations.ToString(CultureInfo.InvariantCulture)
            + "; trackedTagKeys=" + TrackedTagKeys.ToString(CultureInfo.InvariantCulture)
            + "; trackedTagValues=" + TrackedTagValues.ToString(CultureInfo.InvariantCulture)
            + "; incomplete=" + IsIncomplete
            + "; accountingConsistent=" + AccountingIsConsistent;
    }
}

/// <summary>
/// Bounded observed-series accounting for one instrument selection.
/// </summary>
/// <remarks>
/// The tracker never silently undercounts. Once the series safety cap is reached, an observation whose canonical
/// key is not already tracked is reported as <see cref="ProbeRecordOutcome.Exhausted"/>, the tracker records
/// <see cref="SeriesCapExhausted"/>, and the result is explicitly incomplete. Memory stays bounded by the
/// configured caps: no canonical key, tag key, or tag descriptor is retained once a cap is reached.
/// </remarks>
internal sealed class ProbeSeriesTracker
{
    public const int DefaultSeriesCap = 200_000;

    public const int DefaultTagValueCapPerKey = 1_000;

    public const int DefaultTagKeyCap = 64;

    public const int DefaultMaxDescriptorLength = ProbeTagCanonicalizer.DefaultMaxDescriptorLength;

    private const int DiagnosticsSampleLength = 96;

    private readonly object _sync = new object();
    private readonly Dictionary<string, int> _series = new Dictionary<string, int>(StringComparer.Ordinal);

    private readonly Dictionary<string, HashSet<string>> _tagValues =
        new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

    private long _observedMeasurements;
    private long _newSeriesObservations;
    private long _existingSeriesObservations;
    private long _untrackedSeriesObservations;
    private bool _seriesCapExhausted;
    private bool _tagValueCapExhausted;
    private bool _tagKeyCapExhausted;
    private string? _sampleExhaustedSeriesKey;

    public ProbeSeriesTracker(
        int seriesCap = DefaultSeriesCap,
        int tagValueCapPerKey = DefaultTagValueCapPerKey,
        int tagKeyCap = DefaultTagKeyCap,
        int maxDescriptorLength = DefaultMaxDescriptorLength)
    {
        if (seriesCap <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(seriesCap));
        }

        if (tagValueCapPerKey <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(tagValueCapPerKey));
        }

        if (tagKeyCap <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(tagKeyCap));
        }

        SeriesCap = seriesCap;
        TagValueCapPerKey = tagValueCapPerKey;
        TagKeyCap = tagKeyCap;
        MaxDescriptorLength = maxDescriptorLength;
    }

    public int SeriesCap { get; }

    public int TagValueCapPerKey { get; }

    public int TagKeyCap { get; }

    public int MaxDescriptorLength { get; }

    public long ObservedMeasurements
    {
        get
        {
            lock (_sync)
            {
                return _observedMeasurements;
            }
        }
    }

    public long NewSeriesObservations
    {
        get
        {
            lock (_sync)
            {
                return _newSeriesObservations;
            }
        }
    }

    public long ExistingSeriesObservations
    {
        get
        {
            lock (_sync)
            {
                return _existingSeriesObservations;
            }
        }
    }

    public long UntrackedSeriesObservations
    {
        get
        {
            lock (_sync)
            {
                return _untrackedSeriesObservations;
            }
        }
    }

    public bool SeriesCapExhausted
    {
        get
        {
            lock (_sync)
            {
                return _seriesCapExhausted;
            }
        }
    }

    public bool TagValueCapExhausted
    {
        get
        {
            lock (_sync)
            {
                return _tagValueCapExhausted;
            }
        }
    }

    public bool TagKeyCapExhausted
    {
        get
        {
            lock (_sync)
            {
                return _tagKeyCapExhausted;
            }
        }
    }

    public string? SampleExhaustedSeriesKey
    {
        get
        {
            lock (_sync)
            {
                return _sampleExhaustedSeriesKey;
            }
        }
    }

    public int TrackedSeriesCount
    {
        get
        {
            lock (_sync)
            {
                return _series.Count;
            }
        }
    }

    public int TrackedTagKeys
    {
        get
        {
            lock (_sync)
            {
                return _tagValues.Count;
            }
        }
    }

    public int TrackedTagValues
    {
        get
        {
            lock (_sync)
            {
                return CountTrackedTagValues();
            }
        }
    }

    public bool IsIncomplete => SeriesCapExhausted || TagValueCapExhausted || TagKeyCapExhausted;

    /// <summary>
    /// Reads every counter, flag, and dictionary count under the writer lock so the result is a consistent
    /// point-in-time view rather than a mixture of moments.
    /// </summary>
    public ProbeTrackerSnapshot Snapshot()
    {
        lock (_sync)
        {
            return new ProbeTrackerSnapshot(
                _observedMeasurements,
                _newSeriesObservations,
                _existingSeriesObservations,
                _untrackedSeriesObservations,
                _series.Count,
                _tagValues.Count,
                CountTrackedTagValues(),
                SeriesCap,
                TagValueCapPerKey,
                TagKeyCap,
                _seriesCapExhausted,
                _tagValueCapExhausted,
                _tagKeyCapExhausted,
                _sampleExhaustedSeriesKey);
        }
    }

    /// <summary>
    /// Records one delivered measurement. Measurements are delivered on the thread that records them, so the
    /// canonical key is computed outside the lock (it is pure) and every mutation of the series map, the per-tag
    /// value maps, and every counter or flag happens under the lock.
    /// </summary>
    public ProbeRecordOutcome Record(KeyValuePair<string, object?>[] tags)
    {
        string seriesKey = ProbeTagCanonicalizer.CreateSeriesKey(tags, MaxDescriptorLength);

        lock (_sync)
        {
            _observedMeasurements++;

            if (_series.TryGetValue(seriesKey, out int seen))
            {
                _series[seriesKey] = seen + 1;
                _existingSeriesObservations++;
                CountTagValues(tags);
                return ProbeRecordOutcome.ExistingSeries;
            }

            if (_series.Count >= SeriesCap)
            {
                _seriesCapExhausted = true;
                _untrackedSeriesObservations++;
                _sampleExhaustedSeriesKey ??= Truncate(seriesKey);
                return ProbeRecordOutcome.Exhausted;
            }

            _series.Add(seriesKey, 1);
            _newSeriesObservations++;
            CountTagValues(tags);
            return ProbeRecordOutcome.NewSeries;
        }
    }

    public bool TryGetSeriesCount(string seriesKey, out int count)
    {
        lock (_sync)
        {
            return _series.TryGetValue(seriesKey, out count);
        }
    }

    /// <summary>
    /// Looks up distinct values for one encoded tag key, as produced by
    /// <see cref="ProbeTagCanonicalizer.EncodeKeyField"/>.
    /// </summary>
    public bool TryGetTagDistinctValues(string tagKey, out int distinctValues)
    {
        lock (_sync)
        {
            if (_tagValues.TryGetValue(tagKey, out HashSet<string>? values))
            {
                distinctValues = values.Count;
                return true;
            }

            distinctValues = 0;
            return false;
        }
    }

    public string Describe() => Snapshot().Describe();

    private int CountTrackedTagValues()
    {
        int total = 0;
        foreach (HashSet<string> values in _tagValues.Values)
        {
            total += values.Count;
        }

        return total;
    }

    /// <summary>Caller must hold <see cref="_sync"/>.</summary>
    private void CountTagValues(KeyValuePair<string, object?>[] tags)
    {
        for (int i = 0; i < tags.Length; i++)
        {
            string tagKey = ProbeTagCanonicalizer.EncodeKeyField(tags[i].Key);

            if (!_tagValues.TryGetValue(tagKey, out HashSet<string>? values))
            {
                if (_tagValues.Count >= TagKeyCap)
                {
                    _tagKeyCapExhausted = true;
                    continue;
                }

                values = new HashSet<string>(StringComparer.Ordinal);
                _tagValues.Add(tagKey, values);
            }

            string descriptor = ProbeTagCanonicalizer.DescribeValue(tags[i].Value, MaxDescriptorLength);
            if (values.Contains(descriptor))
            {
                continue;
            }

            if (values.Count >= TagValueCapPerKey)
            {
                _tagValueCapExhausted = true;
                continue;
            }

            values.Add(descriptor);
        }
    }

    private static string Truncate(string text)
    {
        return text.Length <= DiagnosticsSampleLength ? text : text.Substring(0, DiagnosticsSampleLength);
    }
}
