// Copyright (c) KeelMatrix

using System.Diagnostics.Metrics;
using KeelMatrix.MetricBudget.Assertions;

namespace KeelMatrix.MetricBudget.Tests;

/// <summary>
/// Observed-series budget behavior and deterministic series identity.
/// </summary>
public sealed class SeriesBudgetTests
{
    [Fact]
    public void PassesWhenObservedSeriesStayWithinBudget()
    {
        string meterName = TestNames.Meter(nameof(PassesWhenObservedSeriesStayWithinBudget));
        using Meter meter = new Meter(meterName, "1.0.0");
        Counter<long> counter = meter.CreateCounter<long>("requests");

        MetricBudgetOptions options = new MetricBudgetOptions()
            .ForInstrument(meterName, "requests", budget => budget.MaxObservedSeries = 4);

        using MetricBudgetSession session = MetricBudgetSession.Start(options);
        for (int i = 0; i < 10; i++)
        {
            counter.Add(1, new KeyValuePair<string, object?>("route", "/a"));
        }

        MetricBudgetReport report = session.Complete();

        Assert.Equal(MetricBudgetOutcome.Passed, report.Outcome);
        Assert.Equal(10, report.TotalMeasurementsObserved);
        Assert.Equal(1, report.ObservedSeriesCount);
        Assert.Empty(report.Violations);
        Assert.True(report.AccountingIsConsistent);
        Assert.True(report.Safety.IsComplete);
        _ = report.AssertWithinBudget();
    }

    [Fact]
    public void FailsWhenObservedSeriesExceedBudget()
    {
        string meterName = TestNames.Meter(nameof(FailsWhenObservedSeriesExceedBudget));
        using Meter meter = new Meter(meterName, "1.0.0");
        Counter<long> counter = meter.CreateCounter<long>("requests");

        MetricBudgetOptions options = new MetricBudgetOptions()
            .ForInstrument(meterName, "requests", budget => budget.MaxObservedSeries = 2);

        using MetricBudgetSession session = MetricBudgetSession.Start(options);
        for (int i = 0; i < 7; i++)
        {
            counter.Add(1, new KeyValuePair<string, object?>("tenant", "tenant-" + i));
        }

        MetricBudgetReport report = session.Complete();

        Assert.Equal(MetricBudgetOutcome.Violation, report.Outcome);
        Assert.False(report.IsWithinBudget);
        Assert.Equal(7, report.ObservedSeriesCount);
        Assert.Equal(1, report.ObservedInstrumentCount);

        MetricBudgetInstrumentResult instrument = report.Rules[0].Instruments[0];
        Assert.Equal(7, instrument.ObservedSeriesCount);
        Assert.Equal(2, instrument.ConfiguredMaxObservedSeries);
        Assert.False(instrument.IsWithinBudget);

        MetricBudgetViolation violation = report.Violations
            .Single(candidate => candidate.Kind == MetricBudgetViolationKind.ObservedSeriesBudgetExceeded);
        Assert.Equal("requests", violation.InstrumentName);
        Assert.Equal(7, violation.ObservedCount);
        Assert.Equal(2, violation.ConfiguredLimit);
    }

    [Fact]
    public void RepeatedMeasurementsOfOneSeriesDoNotIncreaseObservedSeries()
    {
        string meterName = TestNames.Meter(nameof(RepeatedMeasurementsOfOneSeriesDoNotIncreaseObservedSeries));
        using Meter meter = new Meter(meterName, "1.0.0");
        Histogram<double> histogram = meter.CreateHistogram<double>("duration");

        MetricBudgetOptions options = new MetricBudgetOptions()
            .ForInstrument(meterName, "duration", budget => budget.MaxObservedSeries = 1);

        using MetricBudgetSession session = MetricBudgetSession.Start(options);
        for (int i = 0; i < 1_000; i++)
        {
            histogram.Record(i, new KeyValuePair<string, object?>("route", "/a"));
        }

        MetricBudgetReport report = session.Complete();

        Assert.Equal(MetricBudgetOutcome.Passed, report.Outcome);
        Assert.Equal(1_000, report.TotalMeasurementsObserved);
        Assert.Equal(1, report.ObservedSeriesCount);
    }

    [Fact]
    public void TagOrderDoesNotChangeSeriesIdentity()
    {
        string meterName = TestNames.Meter(nameof(TagOrderDoesNotChangeSeriesIdentity));
        using Meter meter = new Meter(meterName, "1.0.0");
        Counter<long> counter = meter.CreateCounter<long>("requests");

        MetricBudgetOptions options = new MetricBudgetOptions()
            .ForInstrument(meterName, "requests", budget => budget.MaxObservedSeries = 2);

        using MetricBudgetSession session = MetricBudgetSession.Start(options);
        counter.Add(
            1,
            new KeyValuePair<string, object?>("route", "/a"),
            new KeyValuePair<string, object?>("status", 200));
        counter.Add(
            1,
            new KeyValuePair<string, object?>("status", 200),
            new KeyValuePair<string, object?>("route", "/a"));
        counter.Add(
            1,
            new KeyValuePair<string, object?>("status", 200),
            new KeyValuePair<string, object?>("route", "/a"),
            new KeyValuePair<string, object?>("route", "/a"));

        MetricBudgetReport report = session.Complete();

        Assert.Equal(MetricBudgetOutcome.Passed, report.Outcome);

        // The first two deliveries are one series; the third repeats a key and is therefore a different series.
        Assert.Equal(2, report.ObservedSeriesCount);
        Assert.Equal(3, report.TotalMeasurementsObserved);
    }

    [Fact]
    public void IdentityIncludesMeterVersionInstrumentNameAndKind()
    {
        string meterName = TestNames.Meter(nameof(IdentityIncludesMeterVersionInstrumentNameAndKind));

        using Meter sameScope = new Meter(meterName, "1.0.0");
        using Meter sameNameSameVersion = new Meter(meterName, "1.0.0");
        using Meter otherVersion = new Meter(meterName, "2.0.0");

        Counter<long> first = sameScope.CreateCounter<long>("requests");
        Counter<long> second = sameNameSameVersion.CreateCounter<long>("requests");
        Counter<long> versioned = otherVersion.CreateCounter<long>("requests");
        Histogram<double> histogram = sameNameSameVersion.CreateHistogram<double>("requests");

        MetricBudgetOptions options = new MetricBudgetOptions()
            .ForInstrument(meterName, "requests", budget => budget.MaxObservedSeries = 4);

        using MetricBudgetSession session = MetricBudgetSession.Start(options);
        KeyValuePair<string, object?>[] tags = { new KeyValuePair<string, object?>("route", "/a") };
        first.Add(1, tags);
        second.Add(1, tags);
        versioned.Add(1, tags);
        histogram.Record(1.5, tags);

        MetricBudgetReport report = session.Complete();

        Assert.Equal(MetricBudgetOutcome.Passed, report.Outcome);

        IReadOnlyList<MetricBudgetInstrumentResult> instruments = report.Rules[0].Instruments;
        Assert.Equal(3, instruments.Count);

        // Two meters with the same name and version share one observed identity, matching the BCL metrics scope
        // model, so their measurements produce one series of two measurements.
        MetricBudgetInstrumentResult shared = instruments.Single(
            instrument => instrument.MeterVersion == "1.0.0" && instrument.InstrumentKind == MetricInstrumentKind.Counter);
        Assert.Equal(2, shared.MeasurementCount);
        Assert.Equal(1, shared.ObservedSeriesCount);

        MetricBudgetInstrumentResult differentVersion = instruments.Single(instrument => instrument.MeterVersion == "2.0.0");
        Assert.Equal(1, differentVersion.MeasurementCount);

        MetricBudgetInstrumentResult differentKind = instruments.Single(
            instrument => instrument.InstrumentKind == MetricInstrumentKind.Histogram);
        Assert.Equal(1, differentKind.MeasurementCount);
    }

    [Fact]
    public void InstrumentsCreatedBeforeAndAfterSessionStartAreObserved()
    {
        string meterName = TestNames.Meter(nameof(InstrumentsCreatedBeforeAndAfterSessionStartAreObserved));
        using Meter meter = new Meter(meterName, "1.0.0");
        Counter<long> beforeStart = meter.CreateCounter<long>("before");

        MetricBudgetOptions options = new MetricBudgetOptions()
            .ForMeter(meterName, budget => budget.MaxObservedSeries = 10);

        using MetricBudgetSession session = MetricBudgetSession.Start(options);

        Counter<long> afterStart = meter.CreateCounter<long>("after");
        beforeStart.Add(1);
        afterStart.Add(1);

        MetricBudgetReport report = session.Complete();

        Assert.Equal(MetricBudgetOutcome.Passed, report.Outcome);
        Assert.Equal(2, report.ObservedInstrumentCount);
        Assert.Equal(2, report.TotalMeasurementsObserved);
        Assert.Equal(2, report.Rules[0].Instruments.Count);
    }

    [Fact]
    public void ObservableInstrumentsAreRecordedOnDemand()
    {
        string meterName = TestNames.Meter(nameof(ObservableInstrumentsAreRecordedOnDemand));
        using Meter meter = new Meter(meterName, "1.0.0");
        int invocations = 0;
        _ = meter.CreateObservableCounter<long>(
            "queue.depth",
            () =>
            {
                invocations++;
                return new Measurement<long>(
                    invocations,
                    new KeyValuePair<string, object?>("queue", "inbound"));
            });

        MetricBudgetOptions options = new MetricBudgetOptions()
            .ForInstrument(meterName, "queue.depth", budget => budget.MaxObservedSeries = 1);

        using MetricBudgetSession session = MetricBudgetSession.Start(options);

        Assert.Equal(0, invocations);
        session.RecordObservableInstruments();
        session.RecordObservableInstruments();

        MetricBudgetReport report = session.Complete();

        Assert.Equal(2, invocations);
        Assert.Equal(MetricBudgetOutcome.Passed, report.Outcome);
        Assert.Equal(2, report.TotalMeasurementsObserved);
        Assert.Equal(1, report.ObservedSeriesCount);
    }
}
