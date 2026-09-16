// Copyright (c) KeelMatrix

using System.Collections.Concurrent;
using System.Diagnostics.Metrics;

namespace KeelMatrix.MetricBudget.Internal;

/// <summary>
/// Single writer path for everything a session counts.
/// </summary>
/// <remarks>
/// <para>
/// Every counter, flag, and map is written under one writer lock, so measurements arriving concurrently from a
/// parallel test suite are accounted exactly and a summary is a single consistent snapshot. Canonicalization runs
/// before the lock because it is pure, which keeps delivery latency from serializing the whole workload.
/// </para>
/// <para>
/// Instrument identity lookup uses a concurrent map that measurement callbacks only read. Entries are added under
/// the writer lock before the instrument is enabled for delivery, so a callback can never observe a half-built
/// account.
/// </para>
/// </remarks>
internal sealed class MetricBudgetState
{
    private readonly FrozenOptions options;
    private readonly object sync = new();
    private readonly Dictionary<InstrumentIdentity, InstrumentAccount> accounts = new();
    private readonly ConcurrentDictionary<Instrument, InstrumentIdentity> identityByInstrument = new();
    private readonly List<Instrument> enabledInstruments = new();
    private readonly Dictionary<InstrumentIdentity, int[]> conflicts = new();

    private long measurementsDelivered;
    private long unmatchedMeasurements;
    private bool stopped;

    internal MetricBudgetState(FrozenOptions options)
    {
        this.options = options;
    }

    internal FrozenOptions Options => options;

    /// <summary>
    /// Handles instrument publication. Selection is decided here, and an instrument is enabled for delivery only
    /// when exactly one rule selects it.
    /// </summary>
    internal void OnInstrumentPublished(Instrument instrument, MeterListener listener)
    {
        InstrumentIdentity identity = InstrumentIdentity.FromInstrument(instrument);
        bool enable = false;

        lock (sync)
        {
            if (stopped)
            {
                return;
            }

            int matchCount = 0;
            int firstMatch = -1;
            for (int i = 0; i < options.Rules.Length; i++)
            {
                if (!options.Rules[i].Matches(identity))
                {
                    continue;
                }

                matchCount++;
                if (firstMatch < 0)
                {
                    firstMatch = i;
                }
            }

            if (matchCount == 0)
            {
                // Not selected: the session never enables delivery for it and never accounts it.
                return;
            }

            if (matchCount > 1)
            {
                if (!conflicts.ContainsKey(identity))
                {
                    int[] matched = new int[matchCount];
                    int position = 0;
                    for (int i = 0; i < options.Rules.Length; i++)
                    {
                        if (options.Rules[i].Matches(identity))
                        {
                            matched[position] = i;
                            position++;
                        }
                    }

                    conflicts.Add(identity, matched);
                }

                // Ambiguous selection is reported as invalid configuration, and nothing is observed for it.
                return;
            }

            if (!accounts.ContainsKey(identity))
            {
                accounts.Add(identity, new InstrumentAccount(identity, firstMatch));
            }

            if (identityByInstrument.TryAdd(instrument, identity))
            {
                enabledInstruments.Add(instrument);
                enable = true;
            }
        }

        if (enable)
        {
            listener.EnableMeasurementEvents(instrument, state: null);
        }
    }

    /// <summary>
    /// Records one delivered measurement for a selected instrument.
    /// </summary>
    /// <typeparam name="T">Measurement type, supplied by the listener callback registration.</typeparam>
    /// <param name="instrument">Instrument that delivered the measurement.</param>
    /// <param name="measurement">Measurement value, which this verifier does not interpret.</param>
    /// <param name="tags">Tag span, valid only for the duration of the callback.</param>
    /// <param name="state">Listener state, unused.</param>
    internal void OnMeasurement<T>(
        Instrument instrument,
        T measurement,
        ReadOnlySpan<KeyValuePair<string, object?>> tags,
        object? state)
    {
        if (!identityByInstrument.TryGetValue(instrument, out InstrumentIdentity identity))
        {
            lock (sync)
            {
                if (!stopped)
                {
                    measurementsDelivered++;
                    unmatchedMeasurements++;
                }
            }

            return;
        }

        // Canonicalization and hashing stay outside the writer lock; they are pure and allocate no shared state.
        string tagSetKey = TagIdentity.CreateTagSetKey(tags, options.MaxTagValueLength, out TagField[] fields);
        Sha256Digest seriesDigest = Sha256TextHash.Digest(TagIdentity.CreateSeriesKey(identity, tagSetKey));

        lock (sync)
        {
            if (stopped)
            {
                return;
            }

            measurementsDelivered++;

            if (!accounts.TryGetValue(identity, out InstrumentAccount? account))
            {
                // A delivered measurement always belongs to a previously created account, so this is an anomaly.
                unmatchedMeasurements++;
                return;
            }

            account.Record(fields, seriesDigest, options);
        }
    }

    /// <summary>
    /// Stops accounting and returns the instruments whose delivery must be disabled explicitly.
    /// </summary>
    /// <remarks>
    /// <c>MeterListener.Dispose</c> does not stop measurement delivery for instruments that a listener already
    /// enabled, so the session disables every one of them before it disposes the listener.
    /// </remarks>
    internal Instrument[] BeginStop()
    {
        lock (sync)
        {
            if (stopped)
            {
                return Array.Empty<Instrument>();
            }

            stopped = true;
            return enabledInstruments.ToArray();
        }
    }

    internal SessionSnapshot CreateSnapshot()
    {
        lock (sync)
        {
            InstrumentAccountSnapshot[] instruments = new InstrumentAccountSnapshot[accounts.Count];
            int index = 0;
            foreach (InstrumentAccount account in accounts.Values)
            {
                instruments[index] = account.CreateSnapshot();
                index++;
            }

            Array.Sort(
                instruments,
                static (left, right) => string.CompareOrdinal(
                    left.Identity.Describe(),
                    right.Identity.Describe()));

            ConfigurationConflictSnapshot[] conflictSnapshots = new ConfigurationConflictSnapshot[conflicts.Count];
            int conflictIndex = 0;
            foreach (KeyValuePair<InstrumentIdentity, int[]> entry in conflicts)
            {
                conflictSnapshots[conflictIndex] = new ConfigurationConflictSnapshot(entry.Key, entry.Value);
                conflictIndex++;
            }

            Array.Sort(
                conflictSnapshots,
                static (left, right) => string.CompareOrdinal(
                    left.Identity.Describe(),
                    right.Identity.Describe()));

            return new SessionSnapshot(
                instruments,
                conflictSnapshots,
                measurementsDelivered,
                unmatchedMeasurements,
                options.MaxTrackedSeries,
                options.MaxTrackedValuesPerTag,
                options.MaxTagValueLength);
        }
    }
}
