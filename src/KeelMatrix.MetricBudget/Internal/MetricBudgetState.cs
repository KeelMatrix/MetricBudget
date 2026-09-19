// Copyright (c) KeelMatrix

using System.Diagnostics.Metrics;

namespace KeelMatrix.MetricBudget.Internal;

/// <summary>
/// Single writer path for everything a session counts.
/// </summary>
/// <remarks>
/// Every counter, flag, and map is written under one writer lock, so measurements arriving concurrently from a
/// parallel test suite are accounted exactly and a summary is a single consistent snapshot. Publication enablement
/// also occurs under this lock, which makes stopping and late publication one coordinated state transition.
/// </remarks>
internal sealed class MetricBudgetState
{
    // Test-only synchronization point for proving that publication cannot pass shutdown while enabling.
    internal static Action? BeforeEnableForTesting { get; set; }

    private readonly FrozenOptions options;
    private readonly object sync = new();
    private readonly Dictionary<InstrumentIdentity, InstrumentAccount> accounts = new();
    private readonly Dictionary<Instrument, InstrumentIdentity> identityByInstrument = new();
    private readonly List<Instrument> enabledInstruments = new();
    private readonly Dictionary<InstrumentIdentity, int[]> conflicts = new();

    private long measurementsDelivered;
    private long unmatchedMeasurements;
    private long untrackedInstrumentIdentities;
    private long untrackedInstrumentInstances;
    private long untrackedConflicts;
    private bool instrumentIdentityTrackingIncomplete;
    private bool instrumentInstanceTrackingIncomplete;
    private bool conflictTrackingIncomplete;
    private bool stopped;
    private int activeMeasurements;

    internal MetricBudgetState(FrozenOptions options)
    {
        this.options = options;
    }

    internal FrozenOptions Options => options;

    /// <summary>
    /// Handles instrument publication. Selection, bounded admission, and enabling are one locked operation.
    /// </summary>
    internal void OnInstrumentPublished(Instrument instrument, MeterListener listener)
    {
        InstrumentIdentity identity = InstrumentIdentity.FromInstrument(instrument);

        lock (sync)
        {
            if (stopped)
            {
                return;
            }

            if (!identity.HasComponentLengthsAtMost(options.MaxInstrumentIdentityLength))
            {
                instrumentIdentityTrackingIncomplete = true;
                untrackedInstrumentIdentities++;
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
                return;
            }

            if (matchCount > 1)
            {
                if (!conflicts.ContainsKey(identity))
                {
                    // A conflict record retains every matching rule index. Use the conflict bound as the
                    // per-record ceiling as well, so an options object with a very large number of overlapping
                    // rules cannot force one unbounded int[] allocation.
                    if (conflicts.Count >= options.MaxTrackedConflicts || matchCount > options.MaxTrackedConflicts)
                    {
                        conflictTrackingIncomplete = true;
                        untrackedConflicts++;
                    }
                    else
                    {
                        int[] matched = new int[matchCount];
                        int position = 0;
                        for (int i = 0; i < options.Rules.Length; i++)
                        {
                            if (options.Rules[i].Matches(identity))
                            {
                                matched[position++] = i;
                            }
                        }

                        conflicts.Add(identity, matched);
                    }
                }

                // Ambiguous selection is reported as invalid configuration, and nothing is observed for it.
                return;
            }

            bool alreadyKnownIdentity = accounts.ContainsKey(identity);
            if (!alreadyKnownIdentity && accounts.Count >= options.MaxTrackedInstrumentIdentities)
            {
                instrumentIdentityTrackingIncomplete = true;
                untrackedInstrumentIdentities++;
                return;
            }

            if (identityByInstrument.ContainsKey(instrument))
            {
                return;
            }

            if (identityByInstrument.Count >= options.MaxTrackedInstrumentInstances)
            {
                instrumentInstanceTrackingIncomplete = true;
                untrackedInstrumentInstances++;
                return;
            }

            if (!alreadyKnownIdentity)
            {
                accounts.Add(identity, new InstrumentAccount(identity, firstMatch));
            }

            identityByInstrument.Add(instrument, identity);
            enabledInstruments.Add(instrument);

            // Keep this call under the same lock as stopped and the enabled-instrument registry. Complete/Dispose
            // cannot observe an enabled-but-unregistered instrument, and publication cannot enable after stopping.
            BeforeEnableForTesting?.Invoke();
            if (stopped)
            {
                identityByInstrument.Remove(instrument);
                enabledInstruments.RemoveAt(enabledInstruments.Count - 1);
                if (!alreadyKnownIdentity)
                {
                    accounts.Remove(identity);
                }

                return;
            }

            listener.EnableMeasurementEvents(instrument, state: null);
        }
    }

    /// <summary>Records one delivered measurement for a selected instrument.</summary>
    internal void OnMeasurement<T>(
        Instrument instrument,
        T measurement,
        ReadOnlySpan<KeyValuePair<string, object?>> tags,
        object? state)
    {
        InstrumentIdentity identity;
        lock (sync)
        {
            if (stopped)
            {
                return;
            }

            if (!identityByInstrument.TryGetValue(instrument, out identity))
            {
                measurementsDelivered++;
                unmatchedMeasurements++;
                return;
            }

            activeMeasurements++;
        }

        try
        {
            if (!TagIdentity.TryCreateTagSetKey(
                    tags,
                    options.MaxTagValueLength,
                    options.MaxTagCount,
                    options.MaxTagKeyLength,
                    out string tagSetKey,
                    out TagField[] fields))
            {
                _ = TagIdentity.GetLastFailure();
                lock (sync)
                {
                    if (!stopped && accounts.TryGetValue(identity, out InstrumentAccount? failedAccount))
                    {
                        measurementsDelivered++;
                        failedAccount.RecordIncompleteMeasurement();
                    }
                }

                return;
            }

            Sha256Digest seriesDigest = Sha256TextHash.Digest(TagIdentity.CreateSeriesKey(identity, tagSetKey));

            lock (sync)
            {
                if (!stopped)
                {
                    measurementsDelivered++;

                    if (!accounts.TryGetValue(identity, out InstrumentAccount? account))
                    {
                        unmatchedMeasurements++;
                    }
                    else
                    {
                        account.Record(fields, seriesDigest, options);
                    }
                }
            }
        }
        finally
        {
            lock (sync)
            {
                activeMeasurements--;
                if (activeMeasurements == 0)
                {
                    Monitor.PulseAll(sync);
                }
            }
        }
    }

    /// <summary>
    /// Stops accounting and waits for callbacks already admitted before stopping. The returned instruments must be
    /// disabled explicitly by the session.
    /// </summary>
    internal Instrument[] BeginStop()
    {
        lock (sync)
        {
            if (stopped)
            {
                return Array.Empty<Instrument>();
            }

            stopped = true;
            while (activeMeasurements != 0)
            {
                Monitor.Wait(sync);
            }

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
                instruments[index++] = account.CreateSnapshot();
            }

            Array.Sort(
                instruments,
                static (left, right) => string.CompareOrdinal(left.Identity.Describe(), right.Identity.Describe()));

            ConfigurationConflictSnapshot[] conflictSnapshots = new ConfigurationConflictSnapshot[conflicts.Count];
            int conflictIndex = 0;
            foreach (KeyValuePair<InstrumentIdentity, int[]> entry in conflicts)
            {
                conflictSnapshots[conflictIndex++] = new ConfigurationConflictSnapshot(entry.Key, entry.Value);
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
                untrackedInstrumentIdentities,
                untrackedInstrumentInstances,
                untrackedConflicts,
                instrumentIdentityTrackingIncomplete,
                instrumentInstanceTrackingIncomplete,
                conflictTrackingIncomplete,
                options.MaxTrackedSeries,
                options.MaxTrackedValuesPerTag,
                options.MaxTagValueLength,
                options.MaxTrackedInstrumentIdentities,
                options.MaxTrackedInstrumentInstances,
                options.MaxTrackedConflicts,
                options.MaxTrackedTagKeysPerInstrument,
                options.MaxTagCount,
                options.MaxInstrumentIdentityLength,
                options.MaxTagKeyLength);
        }
    }
}
