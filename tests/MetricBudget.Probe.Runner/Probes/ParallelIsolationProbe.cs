using System.Diagnostics.Metrics;
using System.Globalization;

namespace MetricBudget.Probe.Runner.Probes;

/// <summary>
/// Process-global listener behavior when several tests run in parallel inside one process.
/// </summary>
internal static class ParallelIsolationProbe
{
    public static ProbeSectionResult Run()
    {
        ProbeSectionResult result = new ProbeSectionResult("parallel-test isolation");
        SelectedSessions(result);
        UnselectedSessions(result);
        DisposalSemantics(result);
        return result;
    }

    private static void SelectedSessions(ProbeSectionResult result)
    {
        ProbeReport.Section("4.1 parallel sessions that select their own instrument");

        const int workers = 4;
        const int rounds = 3;
        const int seriesPerRound = 5;

        MeterObservationSession[] sessions = new MeterObservationSession[workers];
        Meter[] meters = new Meter[workers];
        Counter<long>[] counters = new Counter<long>[workers];

        for (int i = 0; i < workers; i++)
        {
            string meterName = "probe.parallel.selected." + i.ToString(CultureInfo.InvariantCulture);
            meters[i] = new Meter(meterName, "1.0.0");
            counters[i] = meters[i].CreateCounter<long>("probe.parallel.counter");
            string selected = meterName;
            sessions[i] = new MeterObservationSession(
                "selected-" + i.ToString(CultureInfo.InvariantCulture),
                instrument => string.Equals(instrument.Meter.Name, selected, StringComparison.Ordinal),
                seriesCap: 128,
                tagValueCapPerKey: 64,
                tagKeyCap: 8);
            sessions[i].Start();
        }

        Parallel.For(0, workers, worker =>
        {
            for (int round = 0; round < rounds; round++)
            {
                for (int series = 0; series < seriesPerRound; series++)
                {
                    counters[worker].Add(
                        1,
                        new KeyValuePair<string, object?>("route", "/r" + round.ToString(CultureInfo.InvariantCulture) + "-" + series.ToString(CultureInfo.InvariantCulture)),
                        new KeyValuePair<string, object?>("worker", worker));
                }
            }
        });

        int expectedSeries = rounds * seriesPerRound;
        bool allIsolated = true;
        int maxForeignPublished = 0;
        for (int i = 0; i < workers; i++)
        {
            ProbeObservationSummary summary = sessions[i].Summarize();
            IReadOnlyList<string> published = sessions[i].PublishedInstruments;
            int foreignPublished = published.Count(entry => !entry.StartsWith("probe.parallel.selected." + i.ToString(CultureInfo.InvariantCulture) + "|", StringComparison.Ordinal));
            maxForeignPublished = Math.Max(maxForeignPublished, foreignPublished);

            ProbeReport.KeyValue(
                "session" + i.ToString(CultureInfo.InvariantCulture),
                "observed=" + summary.ObservedMeasurements
                + "; trackedSeries=" + summary.TrackedSeriesCount
                + "; publishedInstruments=" + published.Count
                + "; foreignPublishedInstruments=" + foreignPublished
                + "; incomplete=" + summary.IsIncomplete);

            allIsolated &= summary.ObservedMeasurements == expectedSeries && summary.TrackedSeriesCount == expectedSeries;
        }

        ProbeReport.KeyValue("expectedSeriesPerSession", expectedSeries);
        ProbeReport.KeyValue("maxForeignPublishedInstruments", maxForeignPublished);

        for (int i = 0; i < workers; i++)
        {
            sessions[i].Dispose();
            meters[i].Dispose();
        }

        result.Add(
            "selected parallel sessions do not receive foreign measurements",
            allIsolated ? ProbeVerdict.Pass : ProbeVerdict.Fail,
            "each session observed exactly " + expectedSeries.ToString(CultureInfo.InvariantCulture)
                + " of its own measurements while " + maxForeignPublished.ToString(CultureInfo.InvariantCulture)
                + " foreign instruments were still published to it");
        result.Add(
            "instrument publication is process-global",
            maxForeignPublished > 0 ? ProbeVerdict.Pass : ProbeVerdict.Narrow,
            "InstrumentPublished fires for other parallel tests' instruments; only delivery is scoped by selection");
    }

    private static void UnselectedSessions(ProbeSectionResult result)
    {
        ProbeReport.Section("4.2 parallel sessions without selection");

        const int workers = 4;
        const int seriesPerRound = 5;

        MeterObservationSession[] sessions = new MeterObservationSession[workers];
        Meter[] meters = new Meter[workers];
        Counter<long>[] counters = new Counter<long>[workers];

        for (int i = 0; i < workers; i++)
        {
            string meterName = "probe.parallel.unselected." + i.ToString(CultureInfo.InvariantCulture);
            meters[i] = new Meter(meterName, "1.0.0");
            counters[i] = meters[i].CreateCounter<long>("probe.parallel.counter");
            sessions[i] = new MeterObservationSession(
                "unselected-" + i.ToString(CultureInfo.InvariantCulture),
                selector: null,
                seriesCap: 4096,
                tagValueCapPerKey: 256,
                tagKeyCap: 32);
            sessions[i].Start();
        }

        Parallel.For(0, workers, worker =>
        {
            for (int series = 0; series < seriesPerRound; series++)
            {
                counters[worker].Add(
                    1,
                    new KeyValuePair<string, object?>("route", "/u" + series.ToString(CultureInfo.InvariantCulture)),
                    new KeyValuePair<string, object?>("worker", worker));
            }
        });

        int ownSeries = seriesPerRound;
        int totalSeries = workers * seriesPerRound;
        int minObserved = int.MaxValue;
        for (int i = 0; i < workers; i++)
        {
            ProbeObservationSummary summary = sessions[i].Summarize();
            minObserved = Math.Min(minObserved, (int)summary.ObservedMeasurements);
            ProbeReport.KeyValue(
                "session" + i.ToString(CultureInfo.InvariantCulture),
                "observed=" + summary.ObservedMeasurements
                + "; trackedSeries=" + summary.TrackedSeriesCount
                + "; publishedInstruments=" + sessions[i].PublishedInstruments.Count);
        }

        ProbeReport.KeyValue("ownMeasurementsPerSession", ownSeries);
        ProbeReport.KeyValue("measurementsPerSessionIfIsolated", ownSeries);
        ProbeReport.KeyValue("measurementsPerSessionIfFullyShared", totalSeries);
        ProbeReport.KeyValue("minimumObservedAcrossSessions", minObserved);

        for (int i = 0; i < workers; i++)
        {
            sessions[i].Dispose();
            meters[i].Dispose();
        }

        result.Add(
            "unselected sessions observe each other",
            minObserved > ownSeries ? ProbeVerdict.Pass : ProbeVerdict.Fail,
            "without an instrument selector every parallel session observes the other sessions' measurements "
                + "(minimum observed " + minObserved.ToString(CultureInfo.InvariantCulture)
                + " versus " + ownSeries.ToString(CultureInfo.InvariantCulture) + " own measurements)");
    }

    private static void DisposalSemantics(ProbeSectionResult result)
    {
        ProbeReport.Section("4.3 what disposal actually stops");

        const string meterName = "probe.parallel.disposal";
        using Meter meter = new Meter(meterName, "1.0.0");
        Counter<long> counter = meter.CreateCounter<long>("probe.disposal.counter");

        MeterObservationSession first = new MeterObservationSession(
            "disposal-first",
            instrument => string.Equals(instrument.Meter.Name, meterName, StringComparison.Ordinal),
            seriesCap: 64,
            tagValueCapPerKey: 64,
            tagKeyCap: 8);
        MeterObservationSession second = new MeterObservationSession(
            "disposal-second",
            instrument => string.Equals(instrument.Meter.Name, meterName, StringComparison.Ordinal),
            seriesCap: 64,
            tagValueCapPerKey: 64,
            tagKeyCap: 8);

        first.Start();
        second.Start();
        counter.Add(1);

        long firstBefore = first.Summarize().ObservedMeasurements;
        long secondBefore = second.Summarize().ObservedMeasurements;
        ProbeReport.KeyValue("observedByBothBeforeDispose", firstBefore + "/" + secondBefore);
        ProbeReport.KeyValue("instrumentEnabledWithTwoSessions", counter.Enabled);

        first.Dispose();
        counter.Add(2);

        long firstAfter = first.Summarize().ObservedMeasurements;
        long secondAfter = second.Summarize().ObservedMeasurements;
        ProbeReport.KeyValue("disposedSessionObserved", firstAfter);
        ProbeReport.KeyValue("otherSessionObserved", secondAfter);
        ProbeReport.KeyValue("instrumentEnabledAfterFirstDispose", counter.Enabled);

        second.Dispose();
        counter.Add(3);

        ProbeReport.KeyValue("instrumentEnabledAfterBothDisposed", counter.Enabled);
        ProbeReport.KeyValue("observedAfterBothDisposed", first.Summarize().ObservedMeasurements + "/" + second.Summarize().ObservedMeasurements);

        bool disposedStopped = firstAfter == firstBefore;
        bool otherContinued = secondAfter == secondBefore + 1;
        bool disabledAfterAll = !counter.Enabled;

        ProbeReport.Line(
            "  containment contract: a listener observes InstrumentPublished for every meter in the process and receives "
            + "measurements only for instruments it enabled itself; because MeterListener.Dispose does not disable those "
            + "instruments on .NET 8, a session must explicitly disable every instrument it enabled before disposal; "
            + "an instrument stays enabled while any other listener still enables it");
        ProbeReport.KeyValue(
            "sessionDisabledInstrumentCounts",
            first.DisabledInstrumentCount + "/" + second.DisabledInstrumentCount);

        result.Add(
            "disposal and cross-session containment",
            disposedStopped && otherContinued && disabledAfterAll ? ProbeVerdict.Pass : ProbeVerdict.Narrow,
            "disposed session stopped at " + firstAfter.ToString(CultureInfo.InvariantCulture)
                + ", surviving session advanced to " + secondAfter.ToString(CultureInfo.InvariantCulture)
                + ", instrument enabled after all sessions disposed: " + ProbeReport.Format(counter.Enabled));
    }
}
