// Copyright (c) KeelMatrix

namespace KeelMatrix.MetricBudget.Internal;

/// <summary>
/// Observed-series and per-tag accounting for one instrument identity.
/// </summary>
/// <remarks>
/// Every member of this type is written under the owning <see cref="MetricBudgetState"/> writer lock, so a summary
/// built from it is a single consistent snapshot. Distinct series and distinct tag values are held as fixed-size
/// digests, so no raw tag value is retained.
/// </remarks>
internal sealed class InstrumentAccount
{
    private readonly HashSet<Sha256Digest> series = new();
    private readonly Dictionary<string, TagValueAccount> tagValues = new(StringComparer.Ordinal);

    internal InstrumentAccount(InstrumentIdentity identity, int ruleIndex)
    {
        Identity = identity;
        RuleIndex = ruleIndex;
    }

    internal InstrumentIdentity Identity { get; }

    /// <summary>Index of the single rule that selected this instrument.</summary>
    internal int RuleIndex { get; }

    internal long MeasurementCount { get; private set; }

    internal long NewSeriesObservations { get; private set; }

    internal long ExistingSeriesObservations { get; private set; }

    internal long UntrackedSeriesObservations { get; private set; }

    internal long UntrackedTagSetObservations { get; private set; }

    internal long UntrackedTagKeyObservations { get; private set; }

    internal bool SeriesCapExhausted { get; private set; }

    internal bool TagValueCapExhausted { get; private set; }

    internal bool TagKeyCapExhausted { get; private set; }

    internal bool TagSetTrackingIncomplete { get; private set; }

    internal bool InstrumentTrackingIncomplete { get; private set; }

    internal int ObservedSeriesCount => series.Count;

    internal void Record(TagField[] fields, Sha256Digest seriesDigest, FrozenOptions options)
    {
        MeasurementCount++;

        if (series.Contains(seriesDigest))
        {
            ExistingSeriesObservations++;
        }
        else if (series.Count < options.MaxTrackedSeries)
        {
            series.Add(seriesDigest);
            NewSeriesObservations++;
        }
        else
        {
            // An untracked series can never be reported as an existing one, and the observation is counted.
            SeriesCapExhausted = true;
            UntrackedSeriesObservations++;
        }

        if (series.Count == options.MaxTrackedSeries && SeriesCapExhausted)
        {
            // No tag state can be safely inferred after the series bound is exhausted. In particular, do not
            // admit a new key for every untracked series.
            return;
        }

        for (int i = 0; i < fields.Length; i++)
        {
            TagField field = fields[i];

            if (!tagValues.TryGetValue(field.KeyField, out TagValueAccount? account))
            {
                if (tagValues.Count >= options.MaxTrackedTagKeysPerInstrument)
                {
                    TagKeyCapExhausted = true;
                    UntrackedTagKeyObservations++;
                    continue;
                }

                account = new TagValueAccount();
                tagValues.Add(field.KeyField, account);
            }

            account.Record(field.ValueDigest, options.MaxTrackedValuesPerTag);
        }
    }

    internal void RecordIncompleteMeasurement()
    {
        MeasurementCount++;
        TagSetTrackingIncomplete = true;
        UntrackedTagSetObservations++;
    }

    internal void MarkInstrumentTrackingIncomplete()
    {
        InstrumentTrackingIncomplete = true;
    }

    internal InstrumentAccountSnapshot CreateSnapshot()
    {
        TagValueSnapshot[] tags = new TagValueSnapshot[tagValues.Count];
        int index = 0;
        foreach (KeyValuePair<string, TagValueAccount> entry in tagValues)
        {
            tags[index] = entry.Value.CreateSnapshot(entry.Key);
            index++;
        }

        Array.Sort(tags, static (left, right) => string.CompareOrdinal(left.KeyField, right.KeyField));

        return new InstrumentAccountSnapshot(
            Identity,
            RuleIndex,
            MeasurementCount,
            NewSeriesObservations,
            ExistingSeriesObservations,
            UntrackedSeriesObservations,
            UntrackedTagSetObservations,
            UntrackedTagKeyObservations,
            ObservedSeriesCount,
            SeriesCapExhausted,
            TagValueCapExhausted,
            TagKeyCapExhausted,
            TagSetTrackingIncomplete,
            InstrumentTrackingIncomplete,
            tags);
    }

    /// <summary>
    /// Distinct values observed for one encoded tag key.
    /// </summary>
    private sealed class TagValueAccount
    {
        private readonly HashSet<Sha256Digest> values = new();

        private bool capExhausted;

        private long untrackedValueObservations;

        internal void Record(Sha256Digest valueDigest, int maxTrackedValuesPerTag)
        {
            if (values.Contains(valueDigest))
            {
                return;
            }

            if (values.Count < maxTrackedValuesPerTag)
            {
                values.Add(valueDigest);
                return;
            }

            capExhausted = true;
            untrackedValueObservations++;
        }

        internal TagValueSnapshot CreateSnapshot(string keyField)
        {
            return new TagValueSnapshot(keyField, values.Count, capExhausted, untrackedValueObservations);
        }
    }
}
