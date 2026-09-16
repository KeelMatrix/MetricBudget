using System.Globalization;

namespace MetricBudget.Probe;

internal enum ProbeRecordOutcome
{
    NewSeries,
    ExistingSeries,
    Exhausted,
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

    private readonly Dictionary<string, int> _series = new Dictionary<string, int>(StringComparer.Ordinal);

    private readonly Dictionary<string, HashSet<string>> _tagValues =
        new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

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

    public long ObservedMeasurements { get; private set; }

    public long UntrackedSeriesObservations { get; private set; }

    public bool SeriesCapExhausted { get; private set; }

    public bool TagValueCapExhausted { get; private set; }

    public bool TagKeyCapExhausted { get; private set; }

    public string? SampleExhaustedSeriesKey { get; private set; }

    public int TrackedSeriesCount => _series.Count;

    public int TrackedTagKeys => _tagValues.Count;

    public int TrackedTagValues
    {
        get
        {
            int total = 0;
            foreach (HashSet<string> values in _tagValues.Values)
            {
                total += values.Count;
            }

            return total;
        }
    }

    public bool IsIncomplete => SeriesCapExhausted || TagValueCapExhausted || TagKeyCapExhausted;

    public ProbeRecordOutcome Record(KeyValuePair<string, object?>[] tags)
    {
        ObservedMeasurements++;

        string seriesKey = ProbeTagCanonicalizer.CreateSeriesKey(tags, MaxDescriptorLength);

        if (_series.TryGetValue(seriesKey, out int seen))
        {
            _series[seriesKey] = seen + 1;
            CountTagValues(tags);
            return ProbeRecordOutcome.ExistingSeries;
        }

        if (_series.Count >= SeriesCap)
        {
            SeriesCapExhausted = true;
            UntrackedSeriesObservations++;
            SampleExhaustedSeriesKey ??= Truncate(seriesKey);
            return ProbeRecordOutcome.Exhausted;
        }

        _series.Add(seriesKey, 1);
        CountTagValues(tags);
        return ProbeRecordOutcome.NewSeries;
    }

    public bool TryGetSeriesCount(string seriesKey, out int count) => _series.TryGetValue(seriesKey, out count);

    public bool TryGetTagDistinctValues(string tagKey, out int distinctValues)
    {
        if (_tagValues.TryGetValue(tagKey, out HashSet<string>? values))
        {
            distinctValues = values.Count;
            return true;
        }

        distinctValues = 0;
        return false;
    }

    public string Describe()
    {
        return "observed=" + ObservedMeasurements.ToString(CultureInfo.InvariantCulture)
            + "; trackedSeries=" + TrackedSeriesCount.ToString(CultureInfo.InvariantCulture)
            + "/" + SeriesCap.ToString(CultureInfo.InvariantCulture)
            + "; untrackedSeriesObservations=" + UntrackedSeriesObservations.ToString(CultureInfo.InvariantCulture)
            + "; trackedTagKeys=" + TrackedTagKeys.ToString(CultureInfo.InvariantCulture)
            + "; trackedTagValues=" + TrackedTagValues.ToString(CultureInfo.InvariantCulture)
            + "; incomplete=" + IsIncomplete;
    }

    private void CountTagValues(KeyValuePair<string, object?>[] tags)
    {
        for (int i = 0; i < tags.Length; i++)
        {
            string tagKey = ProbeTagCanonicalizer.DescribeKey(tags[i].Key);

            if (!_tagValues.TryGetValue(tagKey, out HashSet<string>? values))
            {
                if (_tagValues.Count >= TagKeyCap)
                {
                    TagKeyCapExhausted = true;
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
                TagValueCapExhausted = true;
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
