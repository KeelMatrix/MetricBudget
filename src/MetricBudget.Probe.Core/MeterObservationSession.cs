using System.Diagnostics.Metrics;

namespace MetricBudget.Probe;

/// <summary>
/// Immutable view of what one probe session observed.
/// </summary>
public sealed class ProbeObservationSummary
{
    internal ProbeObservationSummary(
        string sessionName,
        long observedMeasurements,
        int trackedSeriesCount,
        long untrackedSeriesObservations,
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
        SessionName = sessionName;
        ObservedMeasurements = observedMeasurements;
        TrackedSeriesCount = trackedSeriesCount;
        UntrackedSeriesObservations = untrackedSeriesObservations;
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

    public string SessionName { get; }

    public long ObservedMeasurements { get; }

    /// <summary>Distinct observed series that are tracked in full.</summary>
    public int TrackedSeriesCount { get; }

    /// <summary>
    /// Observations whose canonical series was not tracked because the safety cap was already reached. This is a
    /// lower bound on the number of untracked distinct series, and it is never reported as a clean match.
    /// </summary>
    public long UntrackedSeriesObservations { get; }

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

    public override string ToString()
    {
        return "session=" + SessionName
            + "; observed=" + ObservedMeasurements
            + "; trackedSeries=" + TrackedSeriesCount + "/" + SeriesCap
            + "; untrackedSeriesObservations=" + UntrackedSeriesObservations
            + "; trackedTagKeys=" + TrackedTagKeys + "/" + TagKeyCap
            + "; trackedTagValues=" + TrackedTagValues
            + "; incomplete=" + IsIncomplete
            + (IsIncomplete
                ? "; boundedState=explicit(seriesCap=" + SeriesCapExhausted
                    + ", tagValueCap=" + TagValueCapExhausted
                    + ", tagKeyCap=" + TagKeyCapExhausted + ")"
                : "; boundedState=within-cap");
    }
}

/// <summary>
/// A <see cref="MeterListener"/>-based observation session that copies tag data out of the measurement callback
/// and records bounded observed-series accounting.
/// </summary>
public sealed class MeterObservationSession : IDisposable
{
    private static readonly string[] MeasurementTypeNames =
    {
        "byte", "short", "int", "long", "float", "double", "decimal",
    };

    private readonly MeterListener _listener = new MeterListener();
    private readonly ProbeSeriesTracker _tracker;
    private readonly Func<Instrument, bool>? _selector;
    private readonly List<string> _published = new List<string>();
    private readonly List<string> _enabled = new List<string>();
    private readonly List<Instrument> _enabledInstruments = new List<Instrument>();
    private readonly object _gate = new object();
    private readonly long[] _callbackCounts = new long[7];

    private int _started;
    private int _disposed;
    private long _copiedTagSets;
    private string? _lastMeasurementType;

    public MeterObservationSession(
        string sessionName,
        Func<Instrument, bool>? selector = null,
        int seriesCap = ProbeSeriesTracker.DefaultSeriesCap,
        int tagValueCapPerKey = ProbeSeriesTracker.DefaultTagValueCapPerKey,
        int tagKeyCap = ProbeSeriesTracker.DefaultTagKeyCap,
        int maxDescriptorLength = ProbeSeriesTracker.DefaultMaxDescriptorLength)
    {
        SessionName = sessionName ?? throw new ArgumentNullException(nameof(sessionName));
        _selector = selector;
        _tracker = new ProbeSeriesTracker(seriesCap, tagValueCapPerKey, tagKeyCap, maxDescriptorLength);
    }

    public string SessionName { get; }

    /// <summary>Instruments published to this session, in publication order.</summary>
    public IReadOnlyList<string> PublishedInstruments
    {
        get
        {
            lock (_gate)
            {
                return _published.ToArray();
            }
        }
    }

    /// <summary>Instruments this session enabled for measurement events.</summary>
    public IReadOnlyList<string> EnabledInstruments
    {
        get
        {
            lock (_gate)
            {
                return _enabled.ToArray();
            }
        }
    }

    /// <summary>Number of measurement callbacks delivered, per delivered measurement type.</summary>
    public IReadOnlyDictionary<string, long> CallbackCounts
    {
        get
        {
            Dictionary<string, long> counts = new Dictionary<string, long>(StringComparer.Ordinal);
            for (int i = 0; i < _callbackCounts.Length; i++)
            {
                long value = Interlocked.Read(ref _callbackCounts[i]);
                if (value > 0)
                {
                    counts[MeasurementTypeNames[i]] = value;
                }
            }

            return counts;
        }
    }

    /// <summary>Number of tag sets copied out of the measurement callback into session-owned memory.</summary>
    public long CopiedTagSets => Interlocked.Read(ref _copiedTagSets);

    /// <summary>Measurement type of the most recently delivered callback.</summary>
    public string? LastMeasurementType => Volatile.Read(ref _lastMeasurementType);

    public bool IsStarted => Volatile.Read(ref _started) == 1;

    public void Start()
    {
        if (Interlocked.Exchange(ref _started, 1) == 1)
        {
            throw new InvalidOperationException("The session has already been started.");
        }

        _listener.InstrumentPublished = OnInstrumentPublished;
        RegisterCallbacks();
        _listener.Start();
    }

    /// <summary>Stops delivering measurement events to this session for one instrument.</summary>
    public void StopListeningTo(Instrument instrument)
    {
        ArgumentNullException.ThrowIfNull(instrument);
        _listener.DisableMeasurementEvents(instrument);
        lock (_gate)
        {
            _enabledInstruments.Remove(instrument);
            DisabledInstrumentCount++;
        }
    }

    /// <summary>Resumes delivering measurement events to this session for one instrument.</summary>
    public void ResumeListeningTo(Instrument instrument)
    {
        ArgumentNullException.ThrowIfNull(instrument);
        _listener.EnableMeasurementEvents(instrument);
        lock (_gate)
        {
            if (!_enabledInstruments.Contains(instrument))
            {
                _enabledInstruments.Add(instrument);
            }
        }
    }

    /// <summary>Instruments this session explicitly disabled again, including during disposal.</summary>
    public int DisabledInstrumentCount { get; private set; }

    /// <summary>Records observable instruments for the whole process for this session.</summary>
    public void RecordObservableInstruments() => _listener.RecordObservableInstruments();

    public ProbeObservationSummary Summarize()
    {
        return new ProbeObservationSummary(
            SessionName,
            _tracker.ObservedMeasurements,
            _tracker.TrackedSeriesCount,
            _tracker.UntrackedSeriesObservations,
            _tracker.TrackedTagKeys,
            _tracker.TrackedTagValues,
            _tracker.SeriesCap,
            _tracker.TagValueCapPerKey,
            _tracker.TagKeyCap,
            _tracker.SeriesCapExhausted,
            _tracker.TagValueCapExhausted,
            _tracker.TagKeyCapExhausted,
            _tracker.SampleExhaustedSeriesKey);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }

        // MeterListener.Dispose does not disable the instruments it enabled on .NET 8, so the session disables
        // them explicitly before disposing the listener.
        Instrument[] instruments;
        lock (_gate)
        {
            instruments = _enabledInstruments.ToArray();
            _enabledInstruments.Clear();
        }

        if (IsStarted)
        {
            foreach (Instrument instrument in instruments)
            {
                try
                {
                    _listener.DisableMeasurementEvents(instrument);
                    lock (_gate)
                    {
                        DisabledInstrumentCount++;
                    }
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
            }
        }

        _listener.Dispose();
    }

    private void RegisterCallbacks()
    {
        _listener.SetMeasurementEventCallback<byte>(OnMeasurement);
        _listener.SetMeasurementEventCallback<short>(OnMeasurement);
        _listener.SetMeasurementEventCallback<int>(OnMeasurement);
        _listener.SetMeasurementEventCallback<long>(OnMeasurement);
        _listener.SetMeasurementEventCallback<float>(OnMeasurement);
        _listener.SetMeasurementEventCallback<double>(OnMeasurement);
        _listener.SetMeasurementEventCallback<decimal>(OnMeasurement);
    }

    private void OnInstrumentPublished(Instrument instrument, MeterListener listener)
    {
        string descriptor = DescribeInstrument(instrument);
        lock (_gate)
        {
            _published.Add(descriptor);
        }

        if (_selector is null || _selector(instrument))
        {
            listener.EnableMeasurementEvents(instrument);
            lock (_gate)
            {
                _enabled.Add(descriptor);
                _enabledInstruments.Add(instrument);
            }
        }
    }

    private void OnMeasurement<T>(
        Instrument instrument,
        T measurement,
        ReadOnlySpan<KeyValuePair<string, object?>> tags,
        object? state)
    {
        int index = MeasurementTypeIndex(typeof(T));
        if (index >= 0)
        {
            Interlocked.Increment(ref _callbackCounts[index]);
            Volatile.Write(ref _lastMeasurementType, MeasurementTypeNames[index]);
        }

        // The span is only valid for the duration of this callback, so the tags are copied out eagerly.
        KeyValuePair<string, object?>[] copied = new KeyValuePair<string, object?>[tags.Length];
        for (int i = 0; i < tags.Length; i++)
        {
            copied[i] = tags[i];
        }

        Interlocked.Increment(ref _copiedTagSets);
        _tracker.Record(copied);
    }

    private static int MeasurementTypeIndex(Type type)
    {
        if (type == typeof(byte))
        {
            return 0;
        }

        if (type == typeof(short))
        {
            return 1;
        }

        if (type == typeof(int))
        {
            return 2;
        }

        if (type == typeof(long))
        {
            return 3;
        }

        if (type == typeof(float))
        {
            return 4;
        }

        if (type == typeof(double))
        {
            return 5;
        }

        return type == typeof(decimal) ? 6 : -1;
    }

    private static string DescribeInstrument(Instrument instrument)
    {
        return instrument.Meter.Name
            + "|" + (instrument.Meter.Version ?? string.Empty)
            + "|" + instrument.Name
            + "|" + instrument.GetType().Name
            + "|observable=" + instrument.IsObservable;
    }
}
