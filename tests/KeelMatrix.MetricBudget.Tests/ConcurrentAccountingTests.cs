// Copyright (c) KeelMatrix

using System.Diagnostics.Metrics;
using KeelMatrix.MetricBudget.Internal;

namespace KeelMatrix.MetricBudget.Tests;

/// <summary>
/// Concurrency: a parallel workload is accounted exactly, and an impossible account fails loudly.
/// </summary>
public sealed class ConcurrentAccountingTests
{
    private const int Workers = 4;
    private const int SeriesPerWorker = 250;
    private const int RepeatPerSeries = 4;

    [Fact]
    public async Task ParallelMeasurementsAreAccountedExactly()
    {
        string meterName = TestNames.Meter(nameof(ParallelMeasurementsAreAccountedExactly));
        using Meter meter = new Meter(meterName, "1.0.0");
        Counter<long> counter = meter.CreateCounter<long>("requests");

        int expectedSeries = Workers * SeriesPerWorker;
        MetricBudgetOptions options = new MetricBudgetOptions
        {
            MaxTrackedSeries = expectedSeries * 4,
        };
        options.ForInstrument(meterName, "requests", budget =>
        {
            budget.MaxObservedSeries = expectedSeries;
            budget.Tag("worker").MaxDistinctValues = Workers;
            budget.Tag("series").MaxDistinctValues = SeriesPerWorker;
        });

        using MetricBudgetSession session = MetricBudgetSession.Start(options);

        using (ManualResetEventSlim gate = new ManualResetEventSlim(initialState: false))
        {
            Task[] workers = new Task[Workers];
            for (int worker = 0; worker < Workers; worker++)
            {
                int workerIndex = worker;
                workers[worker] = Task.Run(
                    () =>
                    {
                        gate.Wait();
                        for (int series = 0; series < SeriesPerWorker; series++)
                        {
                            for (int repeat = 0; repeat < RepeatPerSeries; repeat++)
                            {
                                counter.Add(
                                    1,
                                    new KeyValuePair<string, object?>("worker", workerIndex),
                                    new KeyValuePair<string, object?>("series", series));
                            }
                        }
                    });
            }

            gate.Set();
            await Task.WhenAll(workers);
        }

        MetricBudgetReport report = session.Complete();

        long expectedMeasurements = (long)Workers * SeriesPerWorker * RepeatPerSeries;
        Assert.Equal(expectedMeasurements, report.TotalMeasurementsObserved);
        Assert.Equal(expectedSeries, report.ObservedSeriesCount);
        Assert.Equal(MetricBudgetOutcome.Passed, report.Outcome);
        Assert.True(report.AccountingIsConsistent);

        MetricBudgetInstrumentResult instrument = report.Rules[0].Instruments[0];
        Assert.Equal(expectedMeasurements, instrument.MeasurementCount);
        Assert.Equal(expectedSeries, instrument.ObservedSeriesCount);
        Assert.False(instrument.SeriesTrackingIncomplete);

        MetricBudgetTagResult workerTag = instrument.Tags.Single(tag => tag.Key == "worker");
        Assert.Equal(Workers, workerTag.ObservedDistinctValueCount);
        MetricBudgetTagResult seriesTag = instrument.Tags.Single(tag => tag.Key == "series");
        Assert.Equal(SeriesPerWorker, seriesTag.ObservedDistinctValueCount);
    }

    [Fact]
    public void ImpossibleAccountingStateIsReportedAsInconsistent()
    {
        // The session writes every counter under one lock, so this state cannot occur at runtime. The invariant
        // check is exercised directly so the failure mode is proven loud rather than assumed.
        InstrumentIdentity identity = new InstrumentIdentity("meter", "1.0.0", "instrument", MetricInstrumentKind.Counter);
        InstrumentAccountSnapshot impossible = new InstrumentAccountSnapshot(
            identity,
            ruleIndex: 0,
            measurementCount: 3,
            newSeriesObservations: 5,
            existingSeriesObservations: 1,
            untrackedSeriesObservations: 0,
            observedSeriesCount: 5,
            seriesCapExhausted: false,
            tagValueCapExhausted: false,
            tags: Array.Empty<TagValueSnapshot>());

        SessionSnapshot snapshot = new SessionSnapshot(
            new[] { impossible },
            Array.Empty<ConfigurationConflictSnapshot>(),
            measurementsDelivered: 3,
            unmatchedMeasurements: 0,
            maxTrackedSeries: 100,
            maxTrackedValuesPerTag: 100,
            maxTagValueLength: 256);

        List<string> problems = new List<string>();
        bool consistent = AccountingInvariants.Validate(snapshot, problems);

        Assert.False(consistent);
        Assert.Contains(problems, problem => problem.Contains("outnumber measurements", StringComparison.Ordinal));
        Assert.Contains(problems, problem => problem.Contains("do not equal", StringComparison.Ordinal));
    }

    [Fact]
    public void UnattributedMeasurementsAreReportedAsInconsistent()
    {
        SessionSnapshot snapshot = new SessionSnapshot(
            Array.Empty<InstrumentAccountSnapshot>(),
            Array.Empty<ConfigurationConflictSnapshot>(),
            measurementsDelivered: 4,
            unmatchedMeasurements: 4,
            maxTrackedSeries: 100,
            maxTrackedValuesPerTag: 100,
            maxTagValueLength: 256);

        List<string> problems = new List<string>();

        Assert.False(AccountingInvariants.Validate(snapshot, problems));
        Assert.Contains(
            problems,
            problem => problem.Contains("did not enable", StringComparison.Ordinal));
    }
}
