using System.Globalization;

namespace MetricBudget.Probe.Runner.Probes;

/// <summary>
/// Bounded canonicalization cost and explicit safety-cap behavior at representative and extreme series counts.
/// </summary>
internal static class CardinalityScaleProbe
{
    private static readonly int[] RepresentativeCounts = { 10_000, 100_000, 1_000_000 };

    private const int ExtremeCount = 1_000_000;

    private const int CappedSeriesCap = 250_000;

    public static ProbeSectionResult Run()
    {
        ProbeSectionResult result = new ProbeSectionResult("bounded canonicalization");
        RepresentativeRuns(result);
        SeriesCapBehavior(result);
        TagValueCapBehavior(result);
        CanonicalizationOnly(result);
        return result;
    }

    private static void RepresentativeRuns(ProbeSectionResult result)
    {
        ProbeReport.Section("5.1 distinct canonical series at representative counts");

        foreach (int count in RepresentativeCounts)
        {
            ProbeSeriesTracker tracker = new ProbeSeriesTracker(
                seriesCap: count + 1,
                tagValueCapPerKey: 10_000,
                tagKeyCap: 64);

            ProbeMeasurement measurement = ProbeReport.Measure(() => Populate(tracker, count));
            ProbeReportSummary(tracker, measurement, "count=" + count.ToString(CultureInfo.InvariantCulture));

            bool seriesAccountingComplete = tracker.TrackedSeriesCount == count && !tracker.SeriesCapExhausted;
            result.Add(
                "canonicalization at " + count.ToString(CultureInfo.InvariantCulture) + " distinct series",
                seriesAccountingComplete ? ProbeVerdict.Pass : ProbeVerdict.Fail,
                "trackedSeries=" + tracker.TrackedSeriesCount.ToString(CultureInfo.InvariantCulture)
                    + "; perTagValueCapExhausted=" + ProbeReport.Format(tracker.TagValueCapExhausted)
                    + " (the per-tag distinct-value cap is a separate, explicit bound); " + measurement.Describe());
        }
    }

    private static void SeriesCapBehavior(ProbeSectionResult result)
    {
        ProbeReport.Section("5.2 series safety cap with an explicit bounded-state result");

        ProbeSeriesTracker tracker = new ProbeSeriesTracker(
            seriesCap: CappedSeriesCap,
            tagValueCapPerKey: 10_000,
            tagKeyCap: 64);

        long newSeries = 0;
        long existingSeries = 0;
        long exhausted = 0;

        ProbeMeasurement measurement = ProbeReport.Measure(() =>
        {
            for (int i = 0; i < ExtremeCount; i++)
            {
                ProbeRecordOutcome outcome = tracker.Record(Tags(i));
                switch (outcome)
                {
                    case ProbeRecordOutcome.NewSeries:
                        newSeries++;
                        break;
                    case ProbeRecordOutcome.ExistingSeries:
                        existingSeries++;
                        break;
                    default:
                        exhausted++;
                        break;
                }
            }
        });

        ProbeReportSummary(tracker, measurement, "seriesCap=" + CappedSeriesCap.ToString(CultureInfo.InvariantCulture) + "; generated=" + ExtremeCount.ToString(CultureInfo.InvariantCulture));
        ProbeReport.KeyValue("newSeriesObservations", newSeries);
        ProbeReport.KeyValue("existingSeriesObservations", existingSeries);
        ProbeReport.KeyValue("exhaustedObservations", exhausted);
        ProbeReport.KeyValue("expectedExhaustedObservations", ExtremeCount - CappedSeriesCap);
        ProbeReport.KeyValue("distinctSeriesLowerBound", tracker.TrackedSeriesCount + tracker.UntrackedSeriesObservations);

        ProbeRecordOutcome trackedKeyOutcome = tracker.Record(Tags(0));
        ProbeRecordOutcome untrackedKeyOutcome = tracker.Record(Tags(ExtremeCount + 1));
        ProbeReport.KeyValue("outcomeForAlreadyTrackedKey", trackedKeyOutcome);
        ProbeReport.KeyValue("outcomeForNewKeyAfterCap", untrackedKeyOutcome);
        ProbeReport.KeyValue("exhaustionHonest", untrackedKeyOutcome == ProbeRecordOutcome.Exhausted);

        bool capHeld = tracker.TrackedSeriesCount == CappedSeriesCap
            && newSeries == CappedSeriesCap
            && existingSeries == 0
            && exhausted == ExtremeCount - CappedSeriesCap
            && tracker.SeriesCapExhausted
            && untrackedKeyOutcome == ProbeRecordOutcome.Exhausted;

        result.Add(
            "series cap cannot silently undercount",
            capHeld ? ProbeVerdict.Pass : ProbeVerdict.Fail,
            "at cap " + CappedSeriesCap.ToString(CultureInfo.InvariantCulture)
                + " the tracker reported " + exhausted.ToString(CultureInfo.InvariantCulture)
                + " untracked observations, kept the tracked set at " + tracker.TrackedSeriesCount.ToString(CultureInfo.InvariantCulture)
                + ", and never reported an untracked key as an existing series");
    }

    private static void TagValueCapBehavior(ProbeSectionResult result)
    {
        ProbeReport.Section("5.3 per-tag distinct value cap");

        const int distinctValues = 100_000;
        const int tagValueCap = 10_000;

        ProbeSeriesTracker tracker = new ProbeSeriesTracker(
            seriesCap: distinctValues + 1,
            tagValueCapPerKey: tagValueCap,
            tagKeyCap: 64);

        ProbeMeasurement measurement = ProbeReport.Measure(() =>
        {
            for (int i = 0; i < distinctValues; i++)
            {
                tracker.Record(new[]
                {
                    new KeyValuePair<string, object?>("payload", "v" + i.ToString(CultureInfo.InvariantCulture)),
                });
            }
        });

        ProbeReportSummary(tracker, measurement, "distinctValues=" + distinctValues.ToString(CultureInfo.InvariantCulture) + "; tagValueCap=" + tagValueCap.ToString(CultureInfo.InvariantCulture));
        ProbeReport.KeyValue("trackedTagValues", tracker.TrackedTagValues);
        ProbeReport.KeyValue("tagValueCapExhausted", tracker.TagValueCapExhausted);

        result.Add(
            "per-tag distinct value cap",
            tracker.TrackedTagValues == tagValueCap && tracker.TagValueCapExhausted && tracker.IsIncomplete
                ? ProbeVerdict.Pass
                : ProbeVerdict.Fail,
            "per-tag value tracking stopped at the configured cap and reported an explicit incomplete state");
    }

    private static void CanonicalizationOnly(ProbeSectionResult result)
    {
        ProbeReport.Section("5.4 canonicalization cost without accounting");

        int[] counts = { 100_000, 1_000_000 };
        double[] msPerSeries = new double[counts.Length];
        long[] bytesPerSeries = new long[counts.Length];

        for (int run = 0; run < counts.Length; run++)
        {
            int count = counts[run];
            string sink = string.Empty;
            ProbeMeasurement measurement = ProbeReport.Measure(() =>
            {
                for (int i = 0; i < count; i++)
                {
                    string key = ProbeTagCanonicalizer.CreateSeriesKey(Tags(i));
                    sink = key.Length > sink.Length ? key : sink;
                }
            });

            msPerSeries[run] = measurement.ElapsedMs / count;
            bytesPerSeries[run] = measurement.ThreadAllocatedBytes / count;

            ProbeReport.Line(
                "  count=" + count.ToString(CultureInfo.InvariantCulture)
                + "; msPerSeries=" + msPerSeries[run].ToString("0.00000", CultureInfo.InvariantCulture)
                + "; allocatedBytesPerSeries=" + bytesPerSeries[run].ToString(CultureInfo.InvariantCulture)
                + "; " + measurement.Describe());
        }

        // Ten times the series count must not cost materially more per series. A ratio near 1 is linear; the
        // thresholds leave room for JIT warm-up making the 100k run slower per series and for GC noise in the 1M
        // run, while still failing a superlinear slope.
        double growth = msPerSeries[counts.Length - 1] / msPerSeries[0];
        long maxBytesPerSeries = bytesPerSeries.Max();
        bool bounded = growth <= 4.0 && maxBytesPerSeries <= 4096;
        ProbeVerdict verdict = bounded
            ? (growth <= 2.0 ? ProbeVerdict.Pass : ProbeVerdict.Narrow)
            : ProbeVerdict.Fail;

        ProbeReport.KeyValue("msPerSeriesAt100k", msPerSeries[0].ToString("0.00000", CultureInfo.InvariantCulture));
        ProbeReport.KeyValue("msPerSeriesAt1M", msPerSeries[counts.Length - 1].ToString("0.00000", CultureInfo.InvariantCulture));
        ProbeReport.KeyValue("perSeriesCostGrowthRatio", growth.ToString("0.000", CultureInfo.InvariantCulture));
        ProbeReport.KeyValue("maxAllocatedBytesPerSeries", maxBytesPerSeries);

        result.Add(
            "canonicalization is linear and bounded per series",
            verdict,
            "raising the series count tenfold changed the measured per-series cost by "
                + growth.ToString("0.00", CultureInfo.InvariantCulture) + "x ("
                + msPerSeries[0].ToString("0.00000", CultureInfo.InvariantCulture) + " ms per series at 100k versus "
                + msPerSeries[counts.Length - 1].ToString("0.00000", CultureInfo.InvariantCulture)
                + " ms per series at 1M), and the largest measured allocation was "
                + maxBytesPerSeries.ToString(CultureInfo.InvariantCulture)
                + " bytes per series, which is one ordinal sort of the delivered tag entries plus one key string "
                + "with no retained state");
    }

    private static void Populate(ProbeSeriesTracker tracker, int count)
    {
        for (int i = 0; i < count; i++)
        {
            tracker.Record(Tags(i));
        }
    }

    private static KeyValuePair<string, object?>[] Tags(int index)
    {
        return new[]
        {
            new KeyValuePair<string, object?>("route", "/api/items/" + index.ToString(CultureInfo.InvariantCulture)),
            new KeyValuePair<string, object?>("tenant", "t" + (index % 1000).ToString(CultureInfo.InvariantCulture)),
        };
    }

    private static void ProbeReportSummary(ProbeSeriesTracker tracker, ProbeMeasurement measurement, string configuration)
    {
        ProbeReport.Line("  " + configuration + "; " + tracker.Describe());
        ProbeReport.Line("  " + configuration + "; " + measurement.Describe());
        ProbeReport.KeyValue("peakWorkingSetBytesAfterRun", ProbeReport.CurrentProcessPeakWorkingSetBytes());
    }
}
