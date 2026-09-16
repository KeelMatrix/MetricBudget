using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Threading;

namespace MetricBudget.Probe.NetStandard
{
    /// <summary>
    /// The same observation shape as the net8.0 probe session, compiled for netstandard2.0 against
    /// <c>System.Diagnostics.DiagnosticSource</c>.
    /// </summary>
    /// <remarks>
    /// Compiling this type is itself parity evidence: the surface members used here
    /// (<see cref="MeterListener.InstrumentPublished"/>, <see cref="MeterListener.EnableMeasurementEvents"/>,
    /// <see cref="MeterListener.SetMeasurementEventCallback{T}"/> with the four-argument callback,
    /// <see cref="MeterListener.RecordObservableInstruments"/>, <see cref="TagList"/>, and
    /// <see cref="Instrument.Enabled"/>) must exist on the netstandard2.0 asset of the referenced package.
    /// </remarks>
    public sealed class NetStandardObservationSession : IDisposable
    {
        private static readonly string[] MeasurementTypeNames =
        {
            "byte", "short", "int", "long", "float", "double", "decimal",
        };

        private readonly MeterListener _listener = new MeterListener();
        private readonly ProbeSeriesTracker _tracker;
        private readonly List<string> _published = new List<string>();
        private readonly long[] _callbackCounts = new long[7];
        private readonly object _gate = new object();
        private readonly bool _enabled;

        private long _copiedTagSets;
        private int _disposed;

        public NetStandardObservationSession(
            bool enabled = true,
            int seriesCap = ProbeSeriesTracker.DefaultSeriesCap,
            int tagValueCapPerKey = ProbeSeriesTracker.DefaultTagValueCapPerKey,
            int tagKeyCap = ProbeSeriesTracker.DefaultTagKeyCap,
            int maxDescriptorLength = ProbeSeriesTracker.DefaultMaxDescriptorLength)
        {
            _enabled = enabled;
            _tracker = new ProbeSeriesTracker(seriesCap, tagValueCapPerKey, tagKeyCap, maxDescriptorLength);
        }

        public long ObservedMeasurements => _tracker.ObservedMeasurements;

        public int TrackedSeriesCount => _tracker.TrackedSeriesCount;

        public bool SeriesCapExhausted => _tracker.SeriesCapExhausted;

        public long UntrackedSeriesObservations => _tracker.UntrackedSeriesObservations;

        public long CopiedTagSets => Interlocked.Read(ref _copiedTagSets);

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

        /// <summary>Compile-time parity probe: this property must exist on the netstandard2.0 asset.</summary>
        public static bool IsEnabled(Instrument instrument) => instrument.Enabled;

        /// <summary>Compile-time parity probe: <see cref="TagList"/> must exist on the netstandard2.0 asset.</summary>
        public static void AddWithTagList(Counter<long> counter, long value, string key, string? tagValue)
        {
            TagList tags = default;
            tags.Add(key, tagValue);
            counter.Add(value, tags);
        }

        public void Start()
        {
            _listener.InstrumentPublished = OnInstrumentPublished;
            _listener.SetMeasurementEventCallback<byte>(OnMeasurement);
            _listener.SetMeasurementEventCallback<short>(OnMeasurement);
            _listener.SetMeasurementEventCallback<int>(OnMeasurement);
            _listener.SetMeasurementEventCallback<long>(OnMeasurement);
            _listener.SetMeasurementEventCallback<float>(OnMeasurement);
            _listener.SetMeasurementEventCallback<double>(OnMeasurement);
            _listener.SetMeasurementEventCallback<decimal>(OnMeasurement);
            _listener.Start();
        }

        public void RecordObservableInstruments() => _listener.RecordObservableInstruments();

        public string Describe()
        {
            // One locked snapshot, so the reported numbers belong to the same moment.
            ProbeTrackerSnapshot snapshot = _tracker.Snapshot();

            return "observed=" + snapshot.ObservedMeasurements
                + "; copiedTagSets=" + CopiedTagSets
                + "; trackedSeries=" + snapshot.TrackedSeriesCount + "/" + snapshot.SeriesCap
                + "; untrackedSeriesObservations=" + snapshot.UntrackedSeriesObservations
                + "; seriesCapExhausted=" + snapshot.SeriesCapExhausted
                + "; accountingConsistent=" + (CopiedTagSets == snapshot.ObservedMeasurements
                    && snapshot.TrackedSeriesCount <= snapshot.ObservedMeasurements);
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 1)
            {
                return;
            }

            _listener.Dispose();
        }

        private void OnInstrumentPublished(Instrument instrument, MeterListener listener)
        {
            string descriptor = instrument.Meter.Name + "|" + instrument.Name + "|" + instrument.GetType().Name;
            lock (_gate)
            {
                _published.Add(descriptor);
            }

            if (_enabled)
            {
                listener.EnableMeasurementEvents(instrument);
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
            }

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
    }
}
