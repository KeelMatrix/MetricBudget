// Copyright (c) KeelMatrix

using System.Diagnostics.Metrics;

namespace KeelMatrix.MetricBudget.Tests;

/// <summary>
/// Safety bounds: explosive cardinality stays bounded, and the bounded state is explicit.
/// </summary>
public sealed class SafetyBoundTests
{
    [Fact]
    public void SeriesSafetyBoundIsExplicitAndNeverReportsAPass()
    {
        string meterName = TestNames.Meter(nameof(SeriesSafetyBoundIsExplicitAndNeverReportsAPass));
        using Meter meter = new Meter(meterName, "1.0.0");
        Counter<long> counter = meter.CreateCounter<long>("requests");

        const int seriesBound = 64;
        const int generatedSeries = 1_000;

        MetricBudgetOptions options = new MetricBudgetOptions
        {
            MaxTrackedSeries = seriesBound,
        };
        options.ForInstrument(meterName, "requests", budget => budget.MaxObservedSeries = 10_000);

        using MetricBudgetSession session = MetricBudgetSession.Start(options);
        for (int i = 0; i < generatedSeries; i++)
        {
            counter.Add(1, new KeyValuePair<string, object?>("tenant", i));
        }

        MetricBudgetReport report = session.Complete();

        Assert.Equal(MetricBudgetOutcome.ObservationIncomplete, report.Outcome);
        Assert.False(report.IsWithinBudget);
        Assert.False(report.Safety.IsComplete);
        Assert.True(report.Safety.SeriesTrackingIncomplete);

        MetricBudgetInstrumentResult instrument = report.Rules[0].Instruments[0];
        Assert.Equal(seriesBound, instrument.ObservedSeriesCount);
        Assert.True(instrument.SeriesTrackingIncomplete);
        Assert.Equal(generatedSeries - seriesBound, instrument.UntrackedSeriesObservations);
        Assert.Equal(generatedSeries, report.TotalMeasurementsObserved);
        Assert.True(report.AccountingIsConsistent);

        MetricBudgetViolation violation = report.Violations
            .Single(candidate => candidate.Kind == MetricBudgetViolationKind.SafetyLimitReached);
        Assert.Contains("lower bounds", violation.Description, StringComparison.Ordinal);
    }

    [Fact]
    public void PerTagValueSafetyBoundIsExplicitAndBoundToTheTagKey()
    {
        string meterName = TestNames.Meter(nameof(PerTagValueSafetyBoundIsExplicitAndBoundToTheTagKey));
        using Meter meter = new Meter(meterName, "1.0.0");
        Counter<long> counter = meter.CreateCounter<long>("requests");

        const int valueBound = 16;
        const int generatedValues = 200;

        MetricBudgetOptions options = new MetricBudgetOptions
        {
            MaxTrackedValuesPerTag = valueBound,
        };
        options.ForInstrument(meterName, "requests", budget =>
        {
            budget.MaxObservedSeries = 10_000;
            budget.Tag("tenant").MaxDistinctValues = 10_000;
        });

        using MetricBudgetSession session = MetricBudgetSession.Start(options);
        for (int i = 0; i < generatedValues; i++)
        {
            counter.Add(1, new KeyValuePair<string, object?>("tenant", i));
        }

        MetricBudgetReport report = session.Complete();

        Assert.Equal(MetricBudgetOutcome.ObservationIncomplete, report.Outcome);
        Assert.True(report.Safety.TagValueTrackingIncomplete);
        Assert.True(report.Safety.UntrackedTagValueObservations > 0);

        MetricBudgetTagResult tag = Assert.Single(report.Rules[0].Instruments[0].Tags);
        Assert.Equal("tenant", tag.Key);
        Assert.Equal(valueBound, tag.ObservedDistinctValueCount);
        Assert.True(tag.ValueTrackingIncomplete);
        Assert.False(tag.IsWithinBudget);

        MetricBudgetViolation violation = report.Violations
            .Single(candidate => candidate.Kind == MetricBudgetViolationKind.SafetyLimitReached);
        Assert.Contains("tenant", violation.Description, StringComparison.Ordinal);
        Assert.Contains("lower bound", violation.Description, StringComparison.Ordinal);
    }

    [Fact]
    public void SafetyBoundDoesNotHideADefiniteBudgetViolation()
    {
        string meterName = TestNames.Meter(nameof(SafetyBoundDoesNotHideADefiniteBudgetViolation));
        using Meter meter = new Meter(meterName, "1.0.0");
        Counter<long> counter = meter.CreateCounter<long>("requests");

        MetricBudgetOptions options = new MetricBudgetOptions
        {
            MaxTrackedSeries = 32,
        };
        options.ForInstrument(meterName, "requests", budget => budget.MaxObservedSeries = 8);

        using MetricBudgetSession session = MetricBudgetSession.Start(options);
        for (int i = 0; i < 100; i++)
        {
            counter.Add(1, new KeyValuePair<string, object?>("tenant", i));
        }

        MetricBudgetReport report = session.Complete();

        // A breach that is already proven stays a violation rather than being downgraded to "incomplete".
        Assert.Equal(MetricBudgetOutcome.Violation, report.Outcome);
        Assert.Contains(
            report.Violations,
            violation => violation.Kind == MetricBudgetViolationKind.ObservedSeriesBudgetExceeded);
    }
}
