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
    private readonly HashSet<InstrumentNameKey> untrackedNameOnlyIdentities = new();
    private readonly bool[] ruleTrackingIncomplete;

    private long measurementsDelivered;
    private long unmatchedMeasurements;
    private long untrackedInstrumentIdentities;
    private long untrackedInstrumentInstances;
    private long untrackedInstrumentIdentityLengths;
    private long untrackedConflicts;
    private bool instrumentIdentityTrackingIncomplete;
    private bool instrumentInstanceTrackingIncomplete;
    private bool instrumentIdentityLengthTrackingIncomplete;
    private bool untrackedNameOnlyIdentityIndexOverflowed;
    private bool conflictTrackingIncomplete;
    private bool stopped;
    private int activeMeasurements;

    internal MetricBudgetState(FrozenOptions options)
    {
        this.options = options;
        ruleTrackingIncomplete = new bool[options.Rules.Length];
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

            if (!identity.HasComponentLengthsAtMost(options.MaxInstrumentIdentityLength))
            {
                RememberUntrackedName(identity);
                MarkKnownAccountsWithSameName(identity);
                MarkMatchingRulesIncomplete(identity);
                instrumentIdentityLengthTrackingIncomplete = true;
                untrackedInstrumentIdentityLengths++;
                return;
            }

            if (matchCount > 1)
            {
                MarkMatchingRulesIncomplete(identity);
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
                ruleTrackingIncomplete[firstMatch] = true;
                instrumentIdentityTrackingIncomplete = true;
                untrackedInstrumentIdentities++;
                RememberUntrackedName(identity);
                MarkKnownAccountsWithSameName(identity);
                return;
            }

            if (identityByInstrument.ContainsKey(instrument))
            {
                return;
            }

            if (identityByInstrument.Count >= options.MaxTrackedInstrumentInstances)
            {
                ruleTrackingIncomplete[firstMatch] = true;
                instrumentInstanceTrackingIncomplete = true;
                untrackedInstrumentInstances++;
                MarkKnownAccountsWithSameName(identity);
                return;
            }

            if (!alreadyKnownIdentity)
            {
                InstrumentAccount account = new InstrumentAccount(identity, firstMatch);
                if (untrackedNameOnlyIdentityIndexOverflowed
                    || untrackedNameOnlyIdentities.Contains(new InstrumentNameKey(identity)))
                {
                    account.MarkInstrumentTrackingIncomplete();
                }

                accounts.Add(identity, account);
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
                untrackedInstrumentIdentityLengths,
                untrackedConflicts,
                instrumentIdentityTrackingIncomplete,
                instrumentInstanceTrackingIncomplete,
                instrumentIdentityLengthTrackingIncomplete,
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
                options.MaxTagKeyLength,
                (bool[])ruleTrackingIncomplete.Clone());
        }
    }

    private void MarkMatchingRulesIncomplete(in InstrumentIdentity identity)
    {
        for (int i = 0; i < options.Rules.Length; i++)
        {
            if (options.Rules[i].Matches(identity))
            {
                ruleTrackingIncomplete[i] = true;
            }
        }
    }

    private void RememberUntrackedName(in InstrumentIdentity identity)
    {
        InstrumentNameKey key = new InstrumentNameKey(identity);
        if (untrackedNameOnlyIdentities.Contains(key))
        {
            return;
        }

        // Keep the name-only ambiguity index bounded by the same identity admission budget and by the identity
        // component-length bound. If a meter or instrument name itself is too long, a later same-name identity
        // cannot be admitted either, so retaining it would add memory without changing any result.
        if (identity.MeterName.Length <= options.MaxInstrumentIdentityLength
            && identity.InstrumentName.Length <= options.MaxInstrumentIdentityLength)
        {
            if (untrackedNameOnlyIdentities.Count < options.MaxTrackedInstrumentIdentities)
            {
                untrackedNameOnlyIdentities.Add(key);
            }
            else
            {
                // Keep the index bounded, but remember that a name-only identity was rejected after the index
                // reached its bound. Any later identity admission is then uncertain because it may share that
                // forgotten name.
                untrackedNameOnlyIdentityIndexOverflowed = true;
            }
        }
    }

    private void MarkKnownAccountsWithSameName(in InstrumentIdentity identity)
    {
        foreach (InstrumentAccount account in accounts.Values)
        {
            if (string.Equals(account.Identity.MeterName, identity.MeterName, StringComparison.Ordinal)
                && string.Equals(account.Identity.InstrumentName, identity.InstrumentName, StringComparison.Ordinal))
            {
                account.MarkInstrumentTrackingIncomplete();
            }
        }
    }

    private readonly struct InstrumentNameKey : IEquatable<InstrumentNameKey>
    {
        internal InstrumentNameKey(InstrumentIdentity identity)
        {
            MeterName = identity.MeterName;
            InstrumentName = identity.InstrumentName;
        }

        private string MeterName { get; }

        private string InstrumentName { get; }

        public bool Equals(InstrumentNameKey other)
        {
            return string.Equals(MeterName, other.MeterName, StringComparison.Ordinal)
                && string.Equals(InstrumentName, other.InstrumentName, StringComparison.Ordinal);
        }

        public override bool Equals(object? obj)
        {
            return obj is InstrumentNameKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (StringComparer.Ordinal.GetHashCode(MeterName) * 397)
                    ^ StringComparer.Ordinal.GetHashCode(InstrumentName);
            }
        }
    }
}
