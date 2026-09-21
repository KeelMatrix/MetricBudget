// Copyright (c) KeelMatrix

using System.Diagnostics.Metrics;
using KeelMatrix.MetricBudget.Assertions;
using KeelMatrix.MetricBudget.Internal;

namespace KeelMatrix.MetricBudget.Tests;

/// <summary>
/// Bounded admission, identity, lifecycle, and report behavior.
/// </summary>
public sealed class BoundedObservationTests
{
    [Fact]
    public void ChangingTagKeysAfterSeriesCapDoesNotGrowTagState()
    {
        string meterName = TestNames.Meter(nameof(ChangingTagKeysAfterSeriesCapDoesNotGrowTagState));
        using Meter meter = new Meter(meterName, "1.0.0");
        Counter<long> counter = meter.CreateCounter<long>("requests");
        MetricBudgetOptions options = new MetricBudgetOptions
        {
            MaxTrackedSeries = 1,
            MaxTrackedTagKeysPerInstrument = 1,
        };
        options.ForInstrument(meterName, "requests", budget => budget.MaxObservedSeries = int.MaxValue);

        using MetricBudgetSession session = MetricBudgetSession.Start(options);
        for (int i = 0; i < 100; i++)
        {
            counter.Add(1, new KeyValuePair<string, object?>("key-" + i, i));
        }

        MetricBudgetReport report = session.Complete();
        MetricBudgetInstrumentResult instrument = Assert.Single(report.Rules[0].Instruments);

        Assert.Equal(MetricBudgetOutcome.ObservationIncomplete, report.Outcome);
        Assert.Equal(1, instrument.ObservedSeriesCount);
        Assert.Single(instrument.Tags);
        Assert.True(report.Safety.SeriesTrackingIncomplete);
        Assert.True(report.AccountingIsConsistent);
        MetricBudgetTagResult retainedTag = Assert.Single(instrument.Tags);
        Assert.True(retainedTag.SeriesTrackingIncomplete);
        Assert.True(retainedTag.TagKeyTrackingIncomplete);
        Assert.False(retainedTag.IsWithinBudget);
        Assert.Contains("INCOMPLETE", report.ToDiagnosticString(), StringComparison.Ordinal);
        Assert.Contains("observed series: 1", report.ToDiagnosticString(), StringComparison.Ordinal);
        Assert.Contains("measurements: 100", report.ToDiagnosticString(), StringComparison.Ordinal);
        Assert.Throws<MetricBudgetAssertionException>(
            () => report.AssertObservedSeriesAtMost(meterName, "requests", 1));
        Assert.Throws<MetricBudgetAssertionException>(
            () => report.AssertTagDistinctValuesAtMost(meterName, "requests", "key-0", 1));
    }

    [Fact]
    public void SeriesCapLossContinuesBoundedTagAccountingAndMarksTheTagIncomplete()
    {
        string meterName = TestNames.Meter(nameof(SeriesCapLossContinuesBoundedTagAccountingAndMarksTheTagIncomplete));
        using Meter meter = new Meter(meterName, "1.0.0");
        Counter<long> counter = meter.CreateCounter<long>("requests");
        MetricBudgetOptions options = new MetricBudgetOptions
        {
            MaxTrackedSeries = 1,
        };
        options.ForInstrument(meterName, "requests", budget =>
        {
            budget.MaxObservedSeries = 1;
            budget.Tag("id").MaxDistinctValues = 2;
        });

        using MetricBudgetSession session = MetricBudgetSession.Start(options);
        counter.Add(1, new KeyValuePair<string, object?>("id", "a"));
        counter.Add(1, new KeyValuePair<string, object?>("id", "b"));

        MetricBudgetReport report = session.Complete();
        MetricBudgetTagResult tag = Assert.Single(report.Rules[0].Instruments[0].Tags);

        Assert.Equal(MetricBudgetOutcome.ObservationIncomplete, report.Outcome);
        Assert.Equal(2, tag.ObservedDistinctValueCount);
        Assert.False(tag.ValueTrackingIncomplete);
        Assert.True(tag.SeriesTrackingIncomplete);
        Assert.False(tag.IsWithinBudget);
        Assert.Throws<MetricBudgetAssertionException>(
            () => report.AssertTagDistinctValuesAtMost(meterName, "requests", "id", 2));
    }

    [Fact]
    public void DynamicInstrumentAdmissionIsBoundedAndIncomplete()
    {
        string meterName = TestNames.Meter(nameof(DynamicInstrumentAdmissionIsBoundedAndIncomplete));
        using Meter meter = new Meter(meterName, "1.0.0");
        MetricBudgetOptions options = new MetricBudgetOptions
        {
            MaxTrackedInstrumentIdentities = 1,
        };
        options.ForMeter(meterName, budget => budget.MaxObservedSeries = 10);

        using MetricBudgetSession session = MetricBudgetSession.Start(options);
        Counter<long> first = meter.CreateCounter<long>("first");
        first.Add(1);
        Counter<long> second = meter.CreateCounter<long>("second");
        second.Add(1);

        MetricBudgetReport report = session.Complete();

        Assert.Equal(MetricBudgetOutcome.ObservationIncomplete, report.Outcome);
        Assert.Single(report.Rules[0].Instruments);
        Assert.True(report.Safety.InstrumentTrackingIncomplete);
        Assert.True(report.Safety.UntrackedInstrumentIdentities > 0);
        Assert.Contains(report.Violations, violation => violation.Kind == MetricBudgetViolationKind.SafetyLimitReached);
    }

    [Fact]
    public void RuleResultCannotPassWhenAnAdmittedInstrumentIdentityIsRejected()
    {
        string meterName = TestNames.Meter(nameof(RuleResultCannotPassWhenAnAdmittedInstrumentIdentityIsRejected));
        using Meter meter = new Meter(meterName, "1.0.0");
        MetricBudgetOptions options = new MetricBudgetOptions
        {
            MaxTrackedInstrumentIdentities = 1,
        };
        options.ForMeter(meterName, budget => budget.MaxObservedSeries = 1);

        using MetricBudgetSession session = MetricBudgetSession.Start(options);
        Counter<long> first = meter.CreateCounter<long>("first");
        first.Add(1);
        Counter<long> second = meter.CreateCounter<long>("second");
        second.Add(1, new KeyValuePair<string, object?>("tenant", "tenant-a"));
        second.Add(1, new KeyValuePair<string, object?>("tenant", "tenant-b"));

        MetricBudgetReport report = session.Complete();

        Assert.Equal(MetricBudgetOutcome.ObservationIncomplete, report.Outcome);
        Assert.Single(report.Rules[0].Instruments);
        Assert.Equal("first", report.Rules[0].Instruments[0].InstrumentName);
        Assert.False(report.Rules[0].IsWithinBudget);
    }

    [Fact]
    public void RuleResultCannotPassWhenAPhysicalInstrumentInstanceIsRejected()
    {
        string meterName = TestNames.Meter(nameof(RuleResultCannotPassWhenAPhysicalInstrumentInstanceIsRejected));
        using Meter firstMeter = new Meter(meterName, "1.0.0");
        using Meter secondMeter = new Meter(meterName, "1.0.0");
        Counter<long> first = firstMeter.CreateCounter<long>("requests");
        Counter<long> second = secondMeter.CreateCounter<long>("requests");
        MetricBudgetOptions options = new MetricBudgetOptions
        {
            MaxTrackedInstrumentInstances = 1,
        };
        options.ForMeter(meterName, budget => budget.MaxObservedSeries = 1);

        using MetricBudgetSession session = MetricBudgetSession.Start(options);
        first.Add(1);
        second.Add(1);

        MetricBudgetReport report = session.Complete();

        Assert.Equal(MetricBudgetOutcome.ObservationIncomplete, report.Outcome);
        Assert.Single(report.Rules[0].Instruments);
        Assert.False(report.Rules[0].IsWithinBudget);
    }

    [Fact]
    public void RuleResultCannotPassWhenAnOverlongInstrumentIdentityIsRejected()
    {
        const string meterName = "m";
        using Meter meter = new Meter(meterName);
        Counter<long> first = meter.CreateCounter<long>("a");
        Counter<long> second = meter.CreateCounter<long>("long");
        MetricBudgetOptions options = new MetricBudgetOptions
        {
            MaxInstrumentIdentityLength = 3,
        };
        options.ForMeter(meterName, budget => budget.MaxObservedSeries = 1);

        using MetricBudgetSession session = MetricBudgetSession.Start(options);
        first.Add(1);
        second.Add(1);

        MetricBudgetReport report = session.Complete();

        Assert.Equal(MetricBudgetOutcome.ObservationIncomplete, report.Outcome);
        Assert.Single(report.Rules[0].Instruments);
        Assert.Equal("a", report.Rules[0].Instruments[0].InstrumentName);
        Assert.False(report.Rules[0].IsWithinBudget);
    }

    [Fact]
    public void RuleResultCannotPassWhenAnOverlappingSelectorRejectsAnInstrument()
    {
        string firstMeterName = TestNames.Meter(nameof(RuleResultCannotPassWhenAnOverlappingSelectorRejectsAnInstrument) + ".first");
        string secondMeterName = TestNames.Meter(nameof(RuleResultCannotPassWhenAnOverlappingSelectorRejectsAnInstrument) + ".second");
        using Meter firstMeter = new Meter(firstMeterName, "1.0.0");
        using Meter secondMeter = new Meter(secondMeterName, "1.0.0");
        Counter<long> unaffected = firstMeter.CreateCounter<long>("unaffected");
        Counter<long> retained = secondMeter.CreateCounter<long>("retained");
        Counter<long> ambiguous = secondMeter.CreateCounter<long>("ambiguous");
        MetricBudgetOptions options = new MetricBudgetOptions();
        options.ForInstrument(firstMeterName, "unaffected", budget => budget.MaxObservedSeries = 1);
        options.ForMeter(secondMeterName, budget => budget.MaxObservedSeries = 1);
        options.ForInstrument(secondMeterName, "ambiguous", budget => budget.MaxObservedSeries = 1);

        using MetricBudgetSession session = MetricBudgetSession.Start(options);
        unaffected.Add(1);
        retained.Add(1);
        ambiguous.Add(1);

        MetricBudgetReport report = session.Complete();

        Assert.Equal(MetricBudgetOutcome.InvalidConfiguration, report.Outcome);
        Assert.True(report.Rules[0].IsWithinBudget);
        Assert.False(report.Rules[1].IsWithinBudget);
        Assert.False(report.Rules[2].IsWithinBudget);
    }

    [Fact]
    public void RepeatedSameIdentityInstancesAreBounded()
    {
        string meterName = TestNames.Meter(nameof(RepeatedSameIdentityInstancesAreBounded));
        using Meter firstMeter = new Meter(meterName, "1.0.0");
        using Meter secondMeter = new Meter(meterName, "1.0.0");
        Counter<long> first = firstMeter.CreateCounter<long>("requests");
        Counter<long> second = secondMeter.CreateCounter<long>("requests");
        MetricBudgetOptions options = new MetricBudgetOptions
        {
            MaxTrackedInstrumentInstances = 1,
        };
        options.ForInstrument(meterName, "requests", budget => budget.MaxObservedSeries = 10);

        using MetricBudgetSession session = MetricBudgetSession.Start(options);
        first.Add(1);
        second.Add(1);

        MetricBudgetReport report = session.Complete();

        Assert.Equal(MetricBudgetOutcome.ObservationIncomplete, report.Outcome);
        Assert.Equal(1, report.TotalMeasurementsObserved);
        Assert.True(report.Safety.InstrumentTrackingIncomplete);
        Assert.True(report.Safety.UntrackedInstrumentInstances > 0);
    }

    [Fact]
    public void SameIdentityInstanceAdmissionLossFailsClosedForFocusedAssertions()
    {
        string meterName = TestNames.Meter(nameof(SameIdentityInstanceAdmissionLossFailsClosedForFocusedAssertions));
        using Meter firstMeter = new Meter(meterName, "1.0.0");
        Counter<long> first = firstMeter.CreateCounter<long>("requests");
        MetricBudgetOptions options = new MetricBudgetOptions
        {
            MaxTrackedInstrumentInstances = 1,
        };
        options.ForInstrument(meterName, "requests", budget =>
        {
            budget.MaxObservedSeries = 1;
            budget.Tag("tenant").MaxDistinctValues = 1;
        });

        using MetricBudgetSession session = MetricBudgetSession.Start(options);
        first.Add(1, new KeyValuePair<string, object?>("tenant", "a"));

        using Meter secondMeter = new Meter(meterName, "1.0.0");
        Counter<long> second = secondMeter.CreateCounter<long>("requests");
        second.Add(1, new KeyValuePair<string, object?>("tenant", "b"));

        MetricBudgetReport report = session.Complete();
        MetricBudgetInstrumentResult instrument = Assert.Single(report.Rules[0].Instruments);

        Assert.Equal(MetricBudgetOutcome.ObservationIncomplete, report.Outcome);
        Assert.True(report.Safety.InstrumentTrackingIncomplete);
        Assert.True(instrument.InstrumentTrackingIncomplete);
        Assert.False(instrument.IsWithinBudget);
        MetricBudgetTagResult tag = Assert.Single(instrument.Tags);
        Assert.True(tag.InstrumentTrackingIncomplete);
        Assert.False(tag.IsWithinBudget);
        Assert.Throws<MetricBudgetAssertionException>(
            () => report.AssertWithinBudget());
        Assert.Throws<MetricBudgetAssertionException>(
            () => report.AssertObservedSeriesAtMost(meterName, "requests", 1));
        Assert.Throws<MetricBudgetAssertionException>(
            () => report.AssertTagDistinctValuesAtMost(meterName, "requests", "tenant", 1));
    }

    [Fact]
    public void IdentityAdmissionLossMakesSameNameFocusedAssertionsAmbiguous()
    {
        string meterName = TestNames.Meter(nameof(IdentityAdmissionLossMakesSameNameFocusedAssertionsAmbiguous));
        using Meter retainedMeter = new Meter(meterName, "1.0.0");
        Counter<long> retained = retainedMeter.CreateCounter<long>("requests");
        MetricBudgetOptions options = new MetricBudgetOptions
        {
            MaxTrackedInstrumentIdentities = 1,
        };
        options.ForInstrument(meterName, "requests", budget =>
        {
            budget.MaxObservedSeries = 1;
            budget.Tag("tenant").MaxDistinctValues = 1;
        });

        using MetricBudgetSession session = MetricBudgetSession.Start(options);
        retained.Add(1, new KeyValuePair<string, object?>("tenant", "a"));

        using Meter droppedMeter = new Meter(meterName, "2.0.0");
        Counter<long> dropped = droppedMeter.CreateCounter<long>("requests");
        dropped.Add(1, new KeyValuePair<string, object?>("tenant", "b"));

        MetricBudgetReport report = session.Complete();

        Assert.Equal(MetricBudgetOutcome.ObservationIncomplete, report.Outcome);
        Assert.True(report.Safety.InstrumentTrackingIncomplete);
        Assert.Throws<MetricBudgetAssertionException>(
            () => report.AssertObservedSeriesAtMost(meterName, "requests", 1));
        Assert.Throws<MetricBudgetAssertionException>(
            () => report.AssertTagDistinctValuesAtMost(meterName, "requests", "tenant", 1));
    }

    [Fact]
    public void IdentityLengthRejectionBeforeAdmissionFailsClosedForFocusedAssertions()
    {
        const string meterName = "M";
        const string instrumentName = "x";
        MetricBudgetOptions options = new MetricBudgetOptions
        {
            MaxInstrumentIdentityLength = 8,
        };
        options.ForInstrument(meterName, instrumentName, budget =>
        {
            budget.MaxObservedSeries = 1;
            budget.Tag("tenant").MaxDistinctValues = 1;
        });

        using MetricBudgetSession session = MetricBudgetSession.Start(options);
        using Meter rejectedMeter = new Meter(meterName, "123456789");
        _ = rejectedMeter.CreateCounter<long>(instrumentName);
        using Meter admittedMeter = new Meter(meterName, "1");
        Counter<long> admitted = admittedMeter.CreateCounter<long>(instrumentName);
        admitted.Add(1, new KeyValuePair<string, object?>("tenant", "a"));

        MetricBudgetReport report = session.Complete();
        MetricBudgetInstrumentResult instrument = Assert.Single(report.Rules[0].Instruments);

        Assert.Equal(MetricBudgetOutcome.ObservationIncomplete, report.Outcome);
        Assert.True(instrument.InstrumentTrackingIncomplete);
        Assert.True(Assert.Single(instrument.Tags).InstrumentTrackingIncomplete);
        Assert.Throws<MetricBudgetAssertionException>(
            () => report.AssertObservedSeriesAtMost(meterName, instrumentName, 1));
        Assert.Throws<MetricBudgetAssertionException>(
            () => report.AssertTagDistinctValuesAtMost(meterName, instrumentName, "tenant", 1));
    }

    [Fact]
    public void RejectedNameIndexOverflowFailsClosedForLaterIdentityAdmission()
    {
        const string meterName = "M";
        const string instrumentName = "target";
        MetricBudgetOptions options = new MetricBudgetOptions
        {
            MaxTrackedInstrumentIdentities = 1,
            MaxInstrumentIdentityLength = 8,
        };
        options.ForMeter(meterName, budget =>
        {
            budget.MaxObservedSeries = 1;
            budget.Tag("tenant").MaxDistinctValues = 1;
        });

        using MetricBudgetSession session = MetricBudgetSession.Start(options);
        using (Meter rejectedMeter = new Meter(meterName, "123456789"))
        {
            _ = rejectedMeter.CreateCounter<long>("filler");
            _ = rejectedMeter.CreateCounter<long>(instrumentName);
        }

        using Meter admittedMeter = new Meter(meterName, "1");
        Counter<long> admitted = admittedMeter.CreateCounter<long>(instrumentName);
        admitted.Add(1, new KeyValuePair<string, object?>("tenant", "a"));

        MetricBudgetReport report = session.Complete();
        MetricBudgetInstrumentResult instrument = Assert.Single(report.Rules[0].Instruments);
        MetricBudgetTagResult tag = Assert.Single(instrument.Tags);

        Assert.Equal(MetricBudgetOutcome.ObservationIncomplete, report.Outcome);
        Assert.True(report.Safety.InstrumentTrackingIncomplete);
        Assert.False(report.Safety.IsComplete);
        Assert.True(instrument.InstrumentTrackingIncomplete);
        Assert.False(instrument.IsWithinBudget);
        Assert.True(tag.InstrumentTrackingIncomplete);
        Assert.False(tag.IsWithinBudget);
        Assert.False(report.IsWithinBudget);
        Assert.Throws<MetricBudgetAssertionException>(
            () => report.AssertObservedSeriesAtMost(meterName, instrumentName, 1));
        Assert.Throws<MetricBudgetAssertionException>(
            () => report.AssertTagDistinctValuesAtMost(meterName, instrumentName, "tenant", 1));
    }

    [Fact]
    public void IdentityLengthRejectionAfterAdmissionFailsClosedForFocusedAssertions()
    {
        const string meterName = "M";
        const string instrumentName = "x";
        MetricBudgetOptions options = new MetricBudgetOptions
        {
            MaxInstrumentIdentityLength = 8,
        };
        options.ForInstrument(meterName, instrumentName, budget =>
        {
            budget.MaxObservedSeries = 1;
            budget.Tag("tenant").MaxDistinctValues = 1;
        });

        using MetricBudgetSession session = MetricBudgetSession.Start(options);
        using Meter admittedMeter = new Meter(meterName, "1");
        Counter<long> admitted = admittedMeter.CreateCounter<long>(instrumentName);
        admitted.Add(1, new KeyValuePair<string, object?>("tenant", "a"));
        using Meter rejectedMeter = new Meter(meterName, "123456789");
        _ = rejectedMeter.CreateCounter<long>(instrumentName);

        MetricBudgetReport report = session.Complete();
        MetricBudgetInstrumentResult instrument = Assert.Single(report.Rules[0].Instruments);

        Assert.Equal(MetricBudgetOutcome.ObservationIncomplete, report.Outcome);
        Assert.True(instrument.InstrumentTrackingIncomplete);
        Assert.True(Assert.Single(instrument.Tags).InstrumentTrackingIncomplete);
        Assert.Throws<MetricBudgetAssertionException>(
            () => report.AssertObservedSeriesAtMost(meterName, instrumentName, 1));
        Assert.Throws<MetricBudgetAssertionException>(
            () => report.AssertTagDistinctValuesAtMost(meterName, instrumentName, "tenant", 1));
    }

    [Fact]
    public void UnrelatedIdentityAdmissionLossDoesNotInvalidateFocusedTarget()
    {
        string meterName = TestNames.Meter(nameof(UnrelatedIdentityAdmissionLossDoesNotInvalidateFocusedTarget));
        using Meter meter = new Meter(meterName, "1.0.0");
        Counter<long> target = meter.CreateCounter<long>("target");
        MetricBudgetOptions options = new MetricBudgetOptions
        {
            MaxTrackedInstrumentIdentities = 1,
        };
        options.ForInstrument(meterName, "target", budget =>
        {
            budget.MaxObservedSeries = 1;
            budget.Tag("tenant").MaxDistinctValues = 1;
        });
        options.ForInstrument(meterName, "unrelated", budget => budget.MaxObservedSeries = 1);

        using MetricBudgetSession session = MetricBudgetSession.Start(options);
        target.Add(1, new KeyValuePair<string, object?>("tenant", "a"));
        Counter<long> unrelated = meter.CreateCounter<long>("unrelated");
        unrelated.Add(1);

        MetricBudgetReport report = session.Complete();

        Assert.Equal(MetricBudgetOutcome.ObservationIncomplete, report.Outcome);
        Assert.True(report.Safety.InstrumentTrackingIncomplete);
        MetricBudgetInstrumentResult targetResult = Assert.Single(report.Rules[0].Instruments);
        Assert.False(targetResult.InstrumentTrackingIncomplete);
        Assert.True(targetResult.IsWithinBudget);
        Assert.False(Assert.Single(targetResult.Tags).InstrumentTrackingIncomplete);
        _ = report.AssertObservedSeriesAtMost(meterName, "target", 1);
        _ = report.AssertTagDistinctValuesAtMost(meterName, "target", "tenant", 1);
    }

    [Fact]
    public void UnmeasuredSelectedCounterFailsFocusedSeriesAssertion()
    {
        string meterName = TestNames.Meter(nameof(UnmeasuredSelectedCounterFailsFocusedSeriesAssertion));
        using Meter meter = new Meter(meterName, "1.0.0");
        _ = meter.CreateCounter<long>("requests");
        MetricBudgetOptions options = new MetricBudgetOptions()
            .ForInstrument(meterName, "requests", budget => budget.MaxObservedSeries = 1);

        using MetricBudgetSession session = MetricBudgetSession.Start(options);
        MetricBudgetReport report = session.Complete();

        Assert.Equal(MetricBudgetOutcome.NoMeasurementsObserved, report.Outcome);
        Assert.False(Assert.Single(report.Rules[0].Instruments).IsWithinBudget);
        Assert.Throws<MetricBudgetAssertionException>(
            () => report.AssertObservedSeriesAtMost(meterName, "requests", 1));
        Assert.Throws<MetricBudgetAssertionException>(
            () => report.AssertTagDistinctValuesAtMost(meterName, "requests", "tenant", 1));
    }

    [Fact]
    public void UncollectedObservableFailsFocusedSeriesAssertion()
    {
        string meterName = TestNames.Meter(nameof(UncollectedObservableFailsFocusedSeriesAssertion));
        using Meter meter = new Meter(meterName, "1.0.0");
        _ = meter.CreateObservableCounter("requests", () => 1L);
        MetricBudgetOptions options = new MetricBudgetOptions()
            .ForInstrument(meterName, "requests", budget => budget.MaxObservedSeries = 1);

        using MetricBudgetSession session = MetricBudgetSession.Start(options);
        MetricBudgetReport report = session.Complete();

        Assert.Equal(MetricBudgetOutcome.NoMeasurementsObserved, report.Outcome);
        Assert.False(Assert.Single(report.Rules[0].Instruments).IsWithinBudget);
        Assert.Throws<MetricBudgetAssertionException>(
            () => report.AssertObservedSeriesAtMost(meterName, "requests", 1));
        Assert.Throws<MetricBudgetAssertionException>(
            () => report.AssertTagDistinctValuesAtMost(meterName, "requests", "tenant", 1));
    }

    [Fact]
    public void UnselectedOversizedInstrumentsDoNotInvalidateSelectedSession()
    {
        string oversizedName = new string('o', 65);
        using Meter oversizedBefore = new Meter(oversizedName, "1.0.0");
        _ = oversizedBefore.CreateCounter<long>("unselected-before");

        string meterName = "metricbudget-selected";
        using Meter meter = new Meter(meterName, "1.0.0");
        Counter<long> counter = meter.CreateCounter<long>("requests");
        MetricBudgetOptions options = new MetricBudgetOptions
        {
            MaxInstrumentIdentityLength = 64,
        };
        options.ForInstrument(meterName, "requests", budget => budget.MaxObservedSeries = 1);

        using MetricBudgetSession session = MetricBudgetSession.Start(options);
        counter.Add(1);

        using Meter oversizedAfter = new Meter(new string('p', 65), "1.0.0");
        _ = oversizedAfter.CreateCounter<long>("unselected-after");

        MetricBudgetReport report = session.Complete();

        Assert.Equal(MetricBudgetOutcome.Passed, report.Outcome);
        _ = report.AssertWithinBudget();
    }

    [Fact]
    public void OversizedSelectedInstrumentFailsWithIdentityLengthDiagnostic()
    {
        string meterName = new string('s', 65);
        using Meter meter = new Meter(meterName, "1.0.0");
        Counter<long> counter = meter.CreateCounter<long>("requests");
        MetricBudgetOptions options = new MetricBudgetOptions
        {
            MaxInstrumentIdentityLength = 64,
        };
        options.ForInstrument(meterName, "requests", budget => budget.MaxObservedSeries = 1);

        using MetricBudgetSession session = MetricBudgetSession.Start(options);
        counter.Add(1);
        MetricBudgetReport report = session.Complete();

        Assert.Equal(MetricBudgetOutcome.ObservationIncomplete, report.Outcome);
        Assert.Contains("instrument identity length safety bound", report.ToDiagnosticString(), StringComparison.Ordinal);
        Assert.DoesNotContain("MaxTrackedInstrumentIdentities", report.ToDiagnosticString(), StringComparison.Ordinal);
    }

    [Fact]
    public void OversizedKeysAndTagSetsAreRejectedWithoutRetainedValues()
    {
        string meterName = TestNames.Meter(nameof(OversizedKeysAndTagSetsAreRejectedWithoutRetainedValues));
        using Meter meter = new Meter(meterName, "1.0.0");
        Counter<long> counter = meter.CreateCounter<long>("requests");
        MetricBudgetOptions options = new MetricBudgetOptions
        {
            MaxTagKeyLength = 4,
            MaxTagCount = 1,
        };
        options.ForInstrument(meterName, "requests", budget =>
        {
            budget.MaxObservedSeries = 10;
            budget.Tag("too-long-key").MaxDistinctValues = 1;
            budget.Tag("b").MaxDistinctValues = 1;
        });

        using MetricBudgetSession session = MetricBudgetSession.Start(options);
        counter.Add(
            1,
            new KeyValuePair<string, object?>("too-long-key", "value"),
            new KeyValuePair<string, object?>("b", "value"));

        MetricBudgetReport report = session.Complete();
        MetricBudgetInstrumentResult instrument = Assert.Single(report.Rules[0].Instruments);

        Assert.Equal(MetricBudgetOutcome.ObservationIncomplete, report.Outcome);
        Assert.Equal(0, instrument.ObservedSeriesCount);
        Assert.True(instrument.TagSetTrackingIncomplete);
        Assert.Equal(2, instrument.Tags.Count);
        foreach (MetricBudgetTagResult tag in instrument.Tags)
        {
            Assert.True(tag.TagSetTrackingIncomplete);
            Assert.False(tag.IsWithinBudget);
        }
        Assert.Equal(1, report.TotalMeasurementsObserved);
        Assert.Equal(1, report.ObservedInstrumentCount);
        Assert.Contains("INCOMPLETE", report.ToDiagnosticString(), StringComparison.Ordinal);
        Assert.Contains("observed series: 0", report.ToDiagnosticString(), StringComparison.Ordinal);
        Assert.Contains("measurements: 1", report.ToDiagnosticString(), StringComparison.Ordinal);
        Assert.False(report.IsWithinBudget);
        Assert.Throws<MetricBudgetAssertionException>(
            () => report.AssertObservedSeriesAtMost(meterName, "requests", 10));
    }

    [Fact]
    public void OversizedInstrumentIdentityIsNotRetained()
    {
        string meterName = TestNames.Meter(nameof(OversizedInstrumentIdentityIsNotRetained));
        using Meter meter = new Meter(meterName, "1.0.0");
        Counter<long> counter = meter.CreateCounter<long>("requests");
        MetricBudgetOptions options = new MetricBudgetOptions
        {
            MaxInstrumentIdentityLength = 4,
        };
        options.ForMeter(meterName, budget => budget.MaxObservedSeries = 10);

        using MetricBudgetSession session = MetricBudgetSession.Start(options);
        counter.Add(1);
        MetricBudgetReport report = session.Complete();

        Assert.Equal(MetricBudgetOutcome.ObservationIncomplete, report.Outcome);
        Assert.Empty(report.Rules[0].Instruments);
        Assert.True(report.Safety.InstrumentTrackingIncomplete);
        Assert.True(report.Safety.InstrumentIdentityLengthTrackingIncomplete);
        Assert.Equal(1, report.Safety.UntrackedInstrumentIdentityLengths);
        Assert.Contains("instrument identity length safety bound", report.ToDiagnosticString(), StringComparison.Ordinal);
        Assert.DoesNotContain("MaxTrackedInstrumentIdentities", report.ToDiagnosticString(), StringComparison.Ordinal);
    }

    [Fact]
    public void OversizedConflictRecordIsDroppedAsIncomplete()
    {
        string meterName = TestNames.Meter(nameof(OversizedConflictRecordIsDroppedAsIncomplete));
        using Meter meter = new Meter(meterName, "1.0.0");
        Counter<long> counter = meter.CreateCounter<long>("requests");
        MetricBudgetOptions options = new MetricBudgetOptions
        {
            MaxTrackedConflicts = 1,
        };
        options.ForMeter(meterName, budget => budget.MaxObservedSeries = 10);
        options.ForInstrument(meterName, "requests", budget => budget.MaxObservedSeries = 10);

        using MetricBudgetSession session = MetricBudgetSession.Start(options);
        counter.Add(1);
        MetricBudgetReport report = session.Complete();

        Assert.Equal(MetricBudgetOutcome.ObservationIncomplete, report.Outcome);
        Assert.True(report.Safety.ConflictTrackingIncomplete);
        Assert.Empty(report.Violations.Where(violation => violation.Kind == MetricBudgetViolationKind.ConfigurationInvalid));
    }

    [Fact]
    public void LosslessNumericAndDateIdentityDoesNotMergeDistinctValues()
    {
        AssertDistinctValuesAreNotMerged(
            nameof(LosslessNumericAndDateIdentityDoesNotMergeDistinctValues) + ".double",
            1d,
            1.0000000000000002d);
        AssertDistinctValuesAreNotMerged(
            nameof(LosslessNumericAndDateIdentityDoesNotMergeDistinctValues) + ".date",
            new DateTime(2026, 9, 19, 12, 0, 0, 1),
            new DateTime(2026, 9, 19, 12, 0, 0, 2));
    }

    [Fact]
    public void LosslessMalformedAndOversizedStringsDoNotMergeDistinctValues()
    {
        AssertDistinctValuesAreNotMerged(
            nameof(LosslessMalformedAndOversizedStringsDoNotMergeDistinctValues) + ".surrogate",
            "\uD800",
            "\uD801");
        AssertDistinctValuesAreNotMerged(
            nameof(LosslessMalformedAndOversizedStringsDoNotMergeDistinctValues) + ".oversized",
            "abcdefgh",
            "abcdefgi",
            maxTagValueLength: 4);
    }

    [Fact]
    public void UnsupportedValueIsIncompleteAndDoesNotCallToString()
    {
        string meterName = TestNames.Meter(nameof(UnsupportedValueIsIncompleteAndDoesNotCallToString));
        using Meter meter = new Meter(meterName, "1.0.0");
        Counter<long> counter = meter.CreateCounter<long>("requests");
        MetricBudgetOptions options = new MetricBudgetOptions();
        options.ForInstrument(meterName, "requests", budget =>
        {
            budget.MaxObservedSeries = 10;
            budget.Tag("value").MaxDistinctValues = 1;
        });

        using MetricBudgetSession session = MetricBudgetSession.Start(options);
        Assert.Null(RecordUnsupported(counter));
        MetricBudgetReport report = session.Complete();

        Assert.Equal(MetricBudgetOutcome.ObservationIncomplete, report.Outcome);
        Assert.True(report.Safety.TagSetTrackingIncomplete);
        MetricBudgetTagResult tag = Assert.Single(report.Rules[0].Instruments[0].Tags);
        Assert.Equal("value", tag.Key);
        Assert.True(tag.TagSetTrackingIncomplete);
        Assert.False(tag.IsWithinBudget);
        Assert.Equal(1, report.TotalMeasurementsObserved);
        Assert.Equal(1, report.ObservedInstrumentCount);
        Assert.Contains("INCOMPLETE", report.ToDiagnosticString(), StringComparison.Ordinal);
        Assert.Contains("measurements: 1", report.ToDiagnosticString(), StringComparison.Ordinal);
        Assert.False(report.IsWithinBudget);
        Assert.Throws<MetricBudgetAssertionException>(
            () => report.AssertObservedSeriesAtMost(meterName, "requests", 10));
    }

    [Fact]
    public void RetainedTagKeyOverflowFailsFocusedAssertions()
    {
        string meterName = TestNames.Meter(nameof(RetainedTagKeyOverflowFailsFocusedAssertions));
        using Meter meter = new Meter(meterName, "1.0.0");
        Counter<long> counter = meter.CreateCounter<long>("requests");
        MetricBudgetOptions options = new MetricBudgetOptions
        {
            MaxTrackedSeries = 10,
            MaxTrackedTagKeysPerInstrument = 1,
        };
        options.ForInstrument(meterName, "requests", budget =>
        {
            budget.MaxObservedSeries = 2;
            budget.Tag("first").MaxDistinctValues = 1;
            budget.Tag("second").MaxDistinctValues = 1;
        });

        using MetricBudgetSession session = MetricBudgetSession.Start(options);
        counter.Add(1, new KeyValuePair<string, object?>("first", "value"));
        counter.Add(1, new KeyValuePair<string, object?>("second", "value"));

        MetricBudgetReport report = session.Complete();
        MetricBudgetInstrumentResult instrument = Assert.Single(report.Rules[0].Instruments);

        Assert.Equal(MetricBudgetOutcome.ObservationIncomplete, report.Outcome);
        Assert.Equal(2, report.ObservedSeriesCount);
        Assert.Equal(2, report.TotalMeasurementsObserved);
        Assert.True(instrument.TagKeyTrackingIncomplete);
        Assert.True(report.Safety.TagKeyTrackingIncomplete);
        Assert.All(instrument.Tags, tag =>
        {
            Assert.True(tag.TagKeyTrackingIncomplete);
            Assert.False(tag.IsWithinBudget);
        });
        Assert.True(report.AccountingIsConsistent);
        Assert.Contains("INCOMPLETE", report.ToDiagnosticString(), StringComparison.Ordinal);
        Assert.Contains("observed series: 2", report.ToDiagnosticString(), StringComparison.Ordinal);
        Assert.Contains("measurements: 2", report.ToDiagnosticString(), StringComparison.Ordinal);
        Assert.False(report.IsWithinBudget);
        Assert.Throws<MetricBudgetAssertionException>(
            () => report.AssertObservedSeriesAtMost(meterName, "requests", 2));
        Assert.Throws<MetricBudgetAssertionException>(
            () => report.AssertTagDistinctValuesAtMost(meterName, "requests", "first", 1));
    }

    [Fact]
    public async Task PublicationAndCompleteAreCoordinated()
    {
        string meterName = TestNames.Meter(nameof(PublicationAndCompleteAreCoordinated));
        using Meter meter = new Meter(meterName, "1.0.0");
        MetricBudgetOptions options = new MetricBudgetOptions();
        options.ForMeter(meterName, budget => budget.MaxObservedSeries = 10);
        using MetricBudgetSession session = MetricBudgetSession.Start(options);

        using ManualResetEventSlim publicationEntered = new ManualResetEventSlim();
        using ManualResetEventSlim releasePublication = new ManualResetEventSlim();
        MetricBudgetState.BeforeEnableForTesting = () =>
        {
            publicationEntered.Set();
            releasePublication.Wait();
        };

        try
        {
            Task<Counter<long>> publication = Task.Run(() => meter.CreateCounter<long>("requests"));
            Assert.True(publicationEntered.Wait(TimeSpan.FromSeconds(5)));
            Task<MetricBudgetReport> completion = Task.Run(session.Complete);

            Task completedOrTimedOut = await Task.WhenAny(completion, Task.Delay(100));
            Assert.NotSame(completion, completedOrTimedOut);
            releasePublication.Set();
            Counter<long> counter = await publication;
            MetricBudgetReport report = await completion;
            counter.Add(1);

            Assert.Equal(MetricBudgetOutcome.NoMeasurementsObserved, report.Outcome);
            Assert.Equal(0, report.TotalMeasurementsObserved);
        }
        finally
        {
            releasePublication.Set();
            MetricBudgetState.BeforeEnableForTesting = null;
        }
    }

    [Fact]
    public void OtherListenerRemainsEnabledWhenMetricBudgetCompletes()
    {
        string meterName = TestNames.Meter(nameof(OtherListenerRemainsEnabledWhenMetricBudgetCompletes));
        using Meter meter = new Meter(meterName, "1.0.0");
        using MeterListener other = new MeterListener();
        int otherMeasurements = 0;
        other.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == meterName)
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };
        other.SetMeasurementEventCallback<long>((_, _, _, _) => Interlocked.Increment(ref otherMeasurements));
        other.Start();

        Counter<long> counter = meter.CreateCounter<long>("requests");
        MetricBudgetOptions options = new MetricBudgetOptions();
        options.ForInstrument(meterName, "requests", budget => budget.MaxObservedSeries = 10);
        using MetricBudgetSession session = MetricBudgetSession.Start(options);

        counter.Add(1);
        MetricBudgetReport report = session.Complete();
        counter.Add(1);

        Assert.Equal(1, report.TotalMeasurementsObserved);
        Assert.Equal(2, otherMeasurements);
        Assert.True(counter.Enabled);
    }

    [Fact]
    public void ObservableCollectionMustBeRequestedByThisSession()
    {
        string meterName = TestNames.Meter(nameof(ObservableCollectionMustBeRequestedByThisSession));
        using Meter meter = new Meter(meterName, "1.0.0");
        ObservableCounter<long> observable = meter.CreateObservableCounter("requests", () => 1L);
        using MeterListener other = new MeterListener();
        other.InstrumentPublished = (instrument, listener) => listener.EnableMeasurementEvents(instrument);
        other.SetMeasurementEventCallback<long>((_, _, _, _) => { });
        other.Start();

        MetricBudgetOptions options = new MetricBudgetOptions();
        options.ForInstrument(meterName, "requests", budget => budget.MaxObservedSeries = 10);
        using MetricBudgetSession session = MetricBudgetSession.Start(options);

        MetricBudgetReport withoutExplicitCollection = session.Complete();
        Assert.Equal(MetricBudgetOutcome.NoMeasurementsObserved, withoutExplicitCollection.Outcome);

        using MetricBudgetSession secondSession = MetricBudgetSession.Start(options);
        secondSession.RecordObservableInstruments();
        MetricBudgetReport withExplicitCollection = secondSession.Complete();

        _ = observable;
        Assert.Equal(MetricBudgetOutcome.Passed, withExplicitCollection.Outcome);
        Assert.Equal(1, withExplicitCollection.TotalMeasurementsObserved);
    }

    [Fact]
    public void CompletedReportGraphCannotBeMutated()
    {
        string meterName = TestNames.Meter(nameof(CompletedReportGraphCannotBeMutated));
        using Meter meter = new Meter(meterName, "1.0.0");
        Counter<long> counter = meter.CreateCounter<long>("requests");
        MetricBudgetOptions options = new MetricBudgetOptions();
        options.ForInstrument(meterName, "requests", budget =>
        {
            budget.MaxObservedSeries = 2;
            budget.Tag("route").MaxDistinctValues = 2;
        });
        using MetricBudgetSession session = MetricBudgetSession.Start(options);
        counter.Add(1, new KeyValuePair<string, object?>("route", "/a"));
        MetricBudgetReport report = session.Complete();
        string diagnostic = report.ToDiagnosticString();

        Assert.Throws<NotSupportedException>(() => ((IList<MetricBudgetRuleResult>)report.Rules).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<MetricBudgetInstrumentResult>)report.Rules[0].Instruments).Clear());
        Assert.Throws<NotSupportedException>(
            () => ((IList<MetricBudgetTagBudget>)report.Rules[0].Rule.Budget.Tags).Clear());
        Assert.Equal(diagnostic, report.ToDiagnosticString());
        Assert.Equal(1, report.ObservedSeriesCount);
        Assert.Equal(2, report.Rules[0].Rule.Budget.MaxObservedSeries);
    }

    [Fact]
    public void FocusedAssertionRejectsAmbiguousMeterVersions()
    {
        string meterName = TestNames.Meter(nameof(FocusedAssertionRejectsAmbiguousMeterVersions));
        using Meter firstMeter = new Meter(meterName, "1.0.0");
        using Meter secondMeter = new Meter(meterName, "2.0.0");
        Counter<long> first = firstMeter.CreateCounter<long>("requests");
        Counter<long> second = secondMeter.CreateCounter<long>("requests");
        MetricBudgetOptions options = new MetricBudgetOptions();
        options.ForInstrument(meterName, "requests", budget => budget.MaxObservedSeries = 10);
        using MetricBudgetSession session = MetricBudgetSession.Start(options);
        first.Add(1, new KeyValuePair<string, object?>("route", "/a"));
        second.Add(1, new KeyValuePair<string, object?>("route", "/a"));
        second.Add(1, new KeyValuePair<string, object?>("route", "/b"));
        MetricBudgetReport report = session.Complete();

        MetricBudgetAssertionException exception = Assert.Throws<MetricBudgetAssertionException>(
            () => report.AssertObservedSeriesAtMost(meterName, "requests", 1));
        Assert.Contains("ambiguous", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FocusedAssertionRejectsAmbiguousInstrumentKinds()
    {
        string meterName = TestNames.Meter(nameof(FocusedAssertionRejectsAmbiguousInstrumentKinds));
        using Meter meter = new Meter(meterName, "1.0.0");
        Counter<long> counter = meter.CreateCounter<long>("requests");
        Histogram<double> histogram = meter.CreateHistogram<double>("requests");
        MetricBudgetOptions options = new MetricBudgetOptions();
        options.ForMeter(meterName, budget => budget.MaxObservedSeries = 10);
        using MetricBudgetSession session = MetricBudgetSession.Start(options);
        counter.Add(1);
        histogram.Record(1, new KeyValuePair<string, object?>("route", "/a"));
        histogram.Record(1, new KeyValuePair<string, object?>("route", "/b"));
        MetricBudgetReport report = session.Complete();

        MetricBudgetAssertionException exception = Assert.Throws<MetricBudgetAssertionException>(
            () => report.AssertObservedSeriesAtMost(meterName, "requests", 1));
        Assert.Contains("ambiguous", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static Exception? RecordUnsupported(Counter<long> counter)
    {
        try
        {
            counter.Add(1, new KeyValuePair<string, object?>("value", new ThrowingValue()));
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    private static void AssertDistinctValuesAreNotMerged(
        string testName,
        object firstValue,
        object secondValue,
        int maxTagValueLength = 256)
    {
        string meterName = TestNames.Meter(testName);
        using Meter meter = new Meter(meterName, "1.0.0");
        Counter<long> counter = meter.CreateCounter<long>("requests");
        MetricBudgetOptions options = new MetricBudgetOptions
        {
            MaxTagValueLength = maxTagValueLength,
        };
        options.ForInstrument(meterName, "requests", budget =>
        {
            budget.MaxObservedSeries = 10;
            budget.Tag("value").MaxDistinctValues = 1;
        });

        using MetricBudgetSession session = MetricBudgetSession.Start(options);
        counter.Add(1, new KeyValuePair<string, object?>("value", firstValue));
        counter.Add(1, new KeyValuePair<string, object?>("value", secondValue));
        MetricBudgetReport report = session.Complete();

        Assert.Equal(MetricBudgetOutcome.Violation, report.Outcome);
        Assert.Equal(2, report.ObservedSeriesCount);
        Assert.Equal(2, report.Rules[0].Instruments[0].Tags[0].ObservedDistinctValueCount);
    }

    private sealed class ThrowingValue
    {
        public override string ToString() => throw new InvalidOperationException("must not be called");
    }
}
