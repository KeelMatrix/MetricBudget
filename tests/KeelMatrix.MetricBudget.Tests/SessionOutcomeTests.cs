// Copyright (c) KeelMatrix

using System.Diagnostics.Metrics;
using KeelMatrix.MetricBudget.Assertions;

namespace KeelMatrix.MetricBudget.Tests;

/// <summary>
/// Invalid configuration, unmatched rules, and zero-data sessions.
/// </summary>
public sealed class SessionOutcomeTests
{
    [Fact]
    public void StartingWithoutRulesThrows()
    {
        MetricBudgetOptions options = new MetricBudgetOptions();

        MetricBudgetConfigurationException exception = Assert.Throws<MetricBudgetConfigurationException>(
            () => MetricBudgetSession.Start(options));

        Assert.Contains("No instrument selection rule", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void StartingWithRuleThatDeclaresNoLimitThrows()
    {
        MetricBudgetOptions options = new MetricBudgetOptions()
            .ForInstrument("some.instrument", budget => _ = budget.Tags.Count);

        MetricBudgetConfigurationException exception = Assert.Throws<MetricBudgetConfigurationException>(
            () => MetricBudgetSession.Start(options));

        Assert.Contains("no budget is configured", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void StartingWithTagBudgetThatHasNoLimitThrows()
    {
        MetricBudgetOptions options = new MetricBudgetOptions();
        options.ForInstrument("some.instrument", budget => _ = budget.Tag("route"));

        MetricBudgetConfigurationException exception = Assert.Throws<MetricBudgetConfigurationException>(
            () => MetricBudgetSession.Start(options));

        Assert.Contains("has no MaxDistinctValues", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void StartingWithNonPositiveSafetyBoundThrows(int bound)
    {
        MetricBudgetOptions options = new MetricBudgetOptions
        {
            MaxTrackedSeries = bound,
        };
        options.ForInstrument("some.instrument", budget => budget.MaxObservedSeries = 1);

        Assert.Throws<MetricBudgetConfigurationException>(() => MetricBudgetSession.Start(options));
    }

    [Fact]
    public void SelectorRejectsEmptyNames()
    {
        Assert.Throws<ArgumentException>(() => InstrumentSelector.Instrument(" "));
        Assert.Throws<ArgumentException>(() => InstrumentSelector.Meter(string.Empty));

        MetricBudgetOptions options = new MetricBudgetOptions();
        Assert.Throws<ArgumentException>(
            () => options.ForInstrument("meter", " ", budget => budget.MaxObservedSeries = 1));
    }

    [Fact]
    public void AmbiguousRulesProduceInvalidConfiguration()
    {
        string meterName = TestNames.Meter(nameof(AmbiguousRulesProduceInvalidConfiguration));
        using Meter meter = new Meter(meterName, "1.0.0");
        Counter<long> counter = meter.CreateCounter<long>("requests");

        MetricBudgetOptions options = new MetricBudgetOptions()
            .ForMeter(meterName, budget => budget.MaxObservedSeries = 1)
            .ForInstrument(meterName, "requests", budget => budget.MaxObservedSeries = 2);

        using MetricBudgetSession session = MetricBudgetSession.Start(options);
        counter.Add(1);

        MetricBudgetReport report = session.Complete();

        Assert.Equal(MetricBudgetOutcome.InvalidConfiguration, report.Outcome);
        Assert.False(report.IsWithinBudget);
        Assert.Equal(0, report.TotalMeasurementsObserved);
        Assert.Equal(0, report.ObservedInstrumentCount);

        MetricBudgetViolation violation = report.Violations
            .Single(candidate => candidate.Kind == MetricBudgetViolationKind.ConfigurationInvalid);
        Assert.Equal("requests", violation.InstrumentName);
        Assert.Contains("matched rules 1, 2", violation.Description, StringComparison.Ordinal);
    }

    [Fact]
    public void RuleThatMatchesNothingProducesNoMatchingInstrument()
    {
        string meterName = TestNames.Meter(nameof(RuleThatMatchesNothingProducesNoMatchingInstrument));
        using Meter meter = new Meter(meterName, "1.0.0");
        _ = meter.CreateCounter<long>("requests");

        MetricBudgetOptions options = new MetricBudgetOptions()
            .ForInstrument(meterName, "unknown.instrument", budget => budget.MaxObservedSeries = 1);

        using MetricBudgetSession session = MetricBudgetSession.Start(options);

        MetricBudgetReport report = session.Complete();

        Assert.Equal(MetricBudgetOutcome.NoMatchingInstrument, report.Outcome);
        Assert.Equal(InstrumentObservationState.NotObserved, report.Rules[0].State);
        Assert.Empty(report.Rules[0].Instruments);
        Assert.Equal(0, report.ObservedSeriesCount);

        MetricBudgetViolation violation = report.Violations
            .Single(candidate => candidate.Kind == MetricBudgetViolationKind.InstrumentNotObserved);
        Assert.Contains("matched no published instrument", violation.Description, StringComparison.Ordinal);
    }

    [Fact]
    public void SelectedInstrumentWithoutMeasurementsProducesNoMeasurementsObserved()
    {
        string meterName = TestNames.Meter(nameof(SelectedInstrumentWithoutMeasurementsProducesNoMeasurementsObserved));
        using Meter meter = new Meter(meterName, "1.0.0");
        _ = meter.CreateCounter<long>("requests");

        MetricBudgetOptions options = new MetricBudgetOptions()
            .ForInstrument(meterName, "requests", budget => budget.MaxObservedSeries = 1);

        using MetricBudgetSession session = MetricBudgetSession.Start(options);

        MetricBudgetReport report = session.Complete();

        Assert.Equal(MetricBudgetOutcome.NoMeasurementsObserved, report.Outcome);
        Assert.Equal(InstrumentObservationState.NoMeasurements, report.Rules[0].State);
        _ = Assert.Single(report.Rules[0].Instruments);
        Assert.Equal(0, report.ObservedInstrumentCount);
        Assert.False(report.Rules[0].Instruments[0].WasObserved);
        Assert.Equal(0, report.TotalMeasurementsObserved);

        MetricBudgetViolation violation = report.Violations
            .Single(candidate => candidate.Kind == MetricBudgetViolationKind.InstrumentProducedNoMeasurements);
        Assert.Contains("delivered no measurements", violation.Description, StringComparison.Ordinal);
    }

    [Fact]
    public void UnmatchedRuleWinsOverAPassingRule()
    {
        string meterName = TestNames.Meter(nameof(UnmatchedRuleWinsOverAPassingRule));
        using Meter meter = new Meter(meterName, "1.0.0");
        Counter<long> counter = meter.CreateCounter<long>("observed");

        MetricBudgetOptions options = new MetricBudgetOptions()
            .ForInstrument(meterName, "observed", budget => budget.MaxObservedSeries = 4)
            .ForInstrument(meterName, "never.created", budget => budget.MaxObservedSeries = 4);

        using MetricBudgetSession session = MetricBudgetSession.Start(options);
        counter.Add(1);

        MetricBudgetReport report = session.Complete();

        // A rule that verified nothing is never reported as a pass, even when another rule was satisfied.
        Assert.Equal(MetricBudgetOutcome.NoMatchingInstrument, report.Outcome);
        Assert.Equal(InstrumentObservationState.Observed, report.Rules[0].State);
        Assert.Equal(InstrumentObservationState.NotObserved, report.Rules[1].State);
        _ = Assert.Throws<MetricBudgetAssertionException>(() => report.AssertWithinBudget());
    }

    [Fact]
    public void AssertionFailureNamesTheInstrumentAndLimits()
    {
        string meterName = TestNames.Meter(nameof(AssertionFailureNamesTheInstrumentAndLimits));
        using Meter meter = new Meter(meterName, "1.0.0");
        Counter<long> counter = meter.CreateCounter<long>("requests");

        MetricBudgetOptions options = new MetricBudgetOptions()
            .ForInstrument(meterName, "requests", budget => budget.MaxObservedSeries = 1);

        using MetricBudgetSession session = MetricBudgetSession.Start(options);
        counter.Add(1, new KeyValuePair<string, object?>("route", "/a"));
        counter.Add(1, new KeyValuePair<string, object?>("route", "/b"));

        MetricBudgetReport report = session.Complete();

        MetricBudgetAssertionException exception = Assert.Throws<MetricBudgetAssertionException>(
            () => report.AssertWithinBudget());

        Assert.Contains("requests", exception.Message, StringComparison.Ordinal);
        Assert.Contains("2 observed series", exception.Message, StringComparison.Ordinal);
        Assert.Contains("configured maximum of 1", exception.Message, StringComparison.Ordinal);
        Assert.Contains("route", exception.Message, StringComparison.Ordinal);
    }
}
