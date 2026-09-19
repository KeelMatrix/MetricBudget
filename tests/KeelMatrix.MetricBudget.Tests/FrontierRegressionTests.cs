// Copyright (c) KeelMatrix

using System.Diagnostics.Metrics;
using KeelMatrix.MetricBudget.Assertions;
using KeelMatrix.MetricBudget.Internal;

namespace KeelMatrix.MetricBudget.Tests;

public sealed class FrontierRegressionTests
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
        options.ForInstrument(meterName, "requests", budget => budget.MaxObservedSeries = 10);

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
        Assert.Empty(instrument.Tags);
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
        options.ForInstrument(meterName, "requests", budget => budget.MaxObservedSeries = 10);

        using MetricBudgetSession session = MetricBudgetSession.Start(options);
        Assert.Null(RecordUnsupported(counter));
        MetricBudgetReport report = session.Complete();

        Assert.Equal(MetricBudgetOutcome.ObservationIncomplete, report.Outcome);
        Assert.True(report.Safety.TagSetTrackingIncomplete);
        Assert.Empty(report.Rules[0].Instruments[0].Tags);
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
