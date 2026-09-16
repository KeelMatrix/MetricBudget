// Copyright (c) KeelMatrix

using System.Diagnostics.Metrics;
using KeelMatrix.MetricBudget.Assertions;

namespace KeelMatrix.MetricBudget.Tests;

/// <summary>
/// Completion, explicit disabling, disposal, and containment.
/// </summary>
public sealed class SessionLifecycleTests
{
    [Fact]
    public void CompletingDisablesMeasurementEventsForEveryEnabledInstrument()
    {
        string meterName = TestNames.Meter(nameof(CompletingDisablesMeasurementEventsForEveryEnabledInstrument));
        using Meter meter = new Meter(meterName, "1.0.0");
        Counter<long> first = meter.CreateCounter<long>("first");
        Counter<long> second = meter.CreateCounter<long>("second");

        MetricBudgetOptions options = new MetricBudgetOptions()
            .ForMeter(meterName, budget => budget.MaxObservedSeries = 4);

        MetricBudgetSession session = MetricBudgetSession.Start(options);
        first.Add(1);
        second.Add(1);

        Assert.True(first.Enabled);
        MetricBudgetReport report = session.Complete();

        Assert.Equal(2, report.TotalMeasurementsObserved);
        Assert.True(session.IsCompleted);

        // MeterListener.Dispose alone does not stop delivery on .NET 8, so the session disables each instrument it
        // enabled. The BCL flag is the observable proof that containment happened.
        Assert.False(first.Enabled);
        Assert.False(second.Enabled);

        session.Dispose();
        Assert.True(session.IsCompleted);
    }

    [Fact]
    public void MeasurementsAfterCompletionAreNotAccounted()
    {
        string meterName = TestNames.Meter(nameof(MeasurementsAfterCompletionAreNotAccounted));
        using Meter meter = new Meter(meterName, "1.0.0");
        Counter<long> counter = meter.CreateCounter<long>("requests");

        MetricBudgetOptions options = new MetricBudgetOptions()
            .ForInstrument(meterName, "requests", budget => budget.MaxObservedSeries = 4);

        using MetricBudgetSession session = MetricBudgetSession.Start(options);
        counter.Add(1, new KeyValuePair<string, object?>("route", "/a"));

        MetricBudgetReport report = session.Complete();
        counter.Add(1, new KeyValuePair<string, object?>("route", "/b"));

        Assert.Equal(1, report.TotalMeasurementsObserved);
        Assert.Equal(1, report.ObservedSeriesCount);
        Assert.Same(report, session.Complete());
    }

    [Fact]
    public void DisposingWithoutCompletingStopsDeliveryAndDiscardsTheReport()
    {
        string meterName = TestNames.Meter(nameof(DisposingWithoutCompletingStopsDeliveryAndDiscardsTheReport));
        using Meter meter = new Meter(meterName, "1.0.0");
        Counter<long> counter = meter.CreateCounter<long>("requests");

        MetricBudgetOptions options = new MetricBudgetOptions()
            .ForInstrument(meterName, "requests", budget => budget.MaxObservedSeries = 4);

        MetricBudgetSession session = MetricBudgetSession.Start(options);
        counter.Add(1);
        session.Dispose();

        Assert.False(counter.Enabled);
        Assert.Throws<ObjectDisposedException>(() => session.Complete());

        // Disposing twice is safe.
        session.Dispose();
    }

    [Fact]
    public void CompletedSessionDoesNotObserveLaterInstruments()
    {
        string meterName = TestNames.Meter(nameof(CompletedSessionDoesNotObserveLaterInstruments));
        using Meter meter = new Meter(meterName, "1.0.0");
        Counter<long> first = meter.CreateCounter<long>("first");

        MetricBudgetOptions options = new MetricBudgetOptions()
            .ForMeter(meterName, budget => budget.MaxObservedSeries = 4);

        using MetricBudgetSession session = MetricBudgetSession.Start(options);
        first.Add(1);
        MetricBudgetReport report = session.Complete();

        Counter<long> late = meter.CreateCounter<long>("late");
        late.Add(1);

        Assert.False(late.Enabled);
        Assert.Equal(1, report.TotalMeasurementsObserved);
        _ = Assert.Single(report.Rules[0].Instruments);
    }

    [Fact]
    public void ParallelSessionsDoNotObserveEachOthersMeasurements()
    {
        string firstMeterName = TestNames.Meter(nameof(ParallelSessionsDoNotObserveEachOthersMeasurements) + ".first");
        string secondMeterName = TestNames.Meter(nameof(ParallelSessionsDoNotObserveEachOthersMeasurements) + ".second");

        using Meter firstMeter = new Meter(firstMeterName, "1.0.0");
        using Meter secondMeter = new Meter(secondMeterName, "1.0.0");
        Counter<long> firstCounter = firstMeter.CreateCounter<long>("requests");
        Counter<long> secondCounter = secondMeter.CreateCounter<long>("requests");

        MetricBudgetOptions firstOptions = new MetricBudgetOptions()
            .ForInstrument(firstMeterName, "requests", budget => budget.MaxObservedSeries = 4);
        MetricBudgetOptions secondOptions = new MetricBudgetOptions()
            .ForInstrument(secondMeterName, "requests", budget => budget.MaxObservedSeries = 4);

        using MetricBudgetSession firstSession = MetricBudgetSession.Start(firstOptions);
        using MetricBudgetSession secondSession = MetricBudgetSession.Start(secondOptions);

        firstCounter.Add(1);
        firstCounter.Add(1);
        secondCounter.Add(1);

        MetricBudgetReport firstReport = firstSession.Complete();
        MetricBudgetReport secondReport = secondSession.Complete();

        Assert.Equal(2, firstReport.TotalMeasurementsObserved);
        Assert.Equal(1, secondReport.TotalMeasurementsObserved);
        Assert.True(firstReport.IsWithinBudget);
        Assert.True(secondReport.IsWithinBudget);
    }

    [Fact]
    public void AssertionHelpersChainAndCoverInstrumentAndTagViews()
    {
        string meterName = TestNames.Meter(nameof(AssertionHelpersChainAndCoverInstrumentAndTagViews));
        using Meter meter = new Meter(meterName, "1.0.0");
        Counter<long> counter = meter.CreateCounter<long>("requests");

        MetricBudgetOptions options = new MetricBudgetOptions()
            .ForInstrument(meterName, "requests", budget =>
            {
                budget.MaxObservedSeries = 4;
                budget.Tag("route").MaxDistinctValues = 2;
            });

        using MetricBudgetSession session = MetricBudgetSession.Start(options);
        counter.Add(1, new KeyValuePair<string, object?>("route", "/a"));
        counter.Add(1, new KeyValuePair<string, object?>("route", "/b"));

        MetricBudgetReport report = session.Complete();

        _ = report
            .AssertWithinBudget()
            .AssertOutcome(MetricBudgetOutcome.Passed)
            .AssertInstrumentObserved(meterName, "requests")
            .AssertObservedSeriesAtMost(meterName, "requests", 2)
            .AssertTagDistinctValuesAtMost(meterName, "requests", "route", 2);

        _ = Assert.Throws<MetricBudgetAssertionException>(
            () => report.AssertTagDistinctValuesAtMost(meterName, "requests", "route", 1));
        _ = Assert.Throws<MetricBudgetAssertionException>(
            () => report.AssertInstrumentObserved(meterName, "other"));
        _ = Assert.Throws<MetricBudgetAssertionException>(
            () => report.AssertOutcome(MetricBudgetOutcome.Violation));
    }
}
