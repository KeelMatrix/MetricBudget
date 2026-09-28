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
            () => report.AssertInstrumentObserved(meterName, "requests"));
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
            () => report.AssertInstrumentObserved(meterName, "requests"));
        Assert.Throws<MetricBudgetAssertionException>(
            () => report.AssertTagDistinctValuesAtMost(meterName, "requests", "id", 2));
    }

    [Fact]
    public void FocusedObservationAssertionsFailClosedWhenTagValueTrackingIsIncomplete()
    {
        string meterName = TestNames.Meter(nameof(FocusedObservationAssertionsFailClosedWhenTagValueTrackingIsIncomplete));
        using Meter meter = new Meter(meterName, "1.0.0");
        Counter<long> configured = meter.CreateCounter<long>("configured");
        Counter<long> unconfigured = meter.CreateCounter<long>("unconfigured");
        MetricBudgetOptions options = new MetricBudgetOptions
        {
            MaxTrackedValuesPerTag = 1,
        };
        options.ForInstrument(meterName, "configured", budget =>
        {
            budget.MaxObservedSeries = 2;
            budget.Tag("id").MaxDistinctValues = 2;
        });
        options.ForInstrument(meterName, "unconfigured", budget => budget.MaxObservedSeries = 2);

        using MetricBudgetSession session = MetricBudgetSession.Start(options);
        configured.Add(1, new KeyValuePair<string, object?>("id", "a"));
        configured.Add(1, new KeyValuePair<string, object?>("id", "b"));
        unconfigured.Add(1, new KeyValuePair<string, object?>("id", "a"));
        unconfigured.Add(1, new KeyValuePair<string, object?>("id", "b"));

        MetricBudgetReport report = session.Complete();
        MetricBudgetInstrumentResult configuredResult = Assert.Single(report.Rules[0].Instruments);
        MetricBudgetInstrumentResult unconfiguredResult = Assert.Single(report.Rules[1].Instruments);

        Assert.Equal(MetricBudgetOutcome.ObservationIncomplete, report.Outcome);
        Assert.True(report.Safety.TagValueTrackingIncomplete);
        Assert.True(Assert.Single(configuredResult.Tags).ValueTrackingIncomplete);
        Assert.True(Assert.Single(unconfiguredResult.Tags).ValueTrackingIncomplete);

        Assert.Throws<MetricBudgetAssertionException>(
            () => report.AssertInstrumentObserved(meterName, "configured"));
        Assert.Throws<MetricBudgetAssertionException>(
            () => report.AssertInstrumentObserved(meterName, "configured", configuredResult.IdentityDiscriminator));
        Assert.Throws<MetricBudgetAssertionException>(
            () => report.AssertInstrumentObserved(meterName, "unconfigured"));
        Assert.Throws<MetricBudgetAssertionException>(
            () => report.AssertInstrumentObserved(meterName, "unconfigured", unconfiguredResult.IdentityDiscriminator));
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
    public void InstrumentIdentitySeparatesSupportedStaticMetadataEquivalenceClasses()
    {
        string meterName = TestNames.Meter(nameof(InstrumentIdentitySeparatesSupportedStaticMetadataEquivalenceClasses));
        KeyValuePair<string, object?> meterTag = new KeyValuePair<string, object?>("scope", "one");
        KeyValuePair<string, object?> instrumentTag = new KeyValuePair<string, object?>("stream", "one");

        using Meter baselineMeter = new Meter(meterName, "1.0.0");
        using Meter scopedMeter = new Meter(new MeterOptions(meterName)
        {
            Version = "1.0.0",
            Scope = new object(),
        });
        using Meter meterTagged = new Meter(new MeterOptions(meterName)
        {
            Version = "1.0.0",
            Tags = new[] { meterTag },
        });
        using Meter unitMeter = new Meter(meterName, "1.0.0");
        using Meter descriptionMeter = new Meter(meterName, "1.0.0");
        using Meter typeMeter = new Meter(meterName, "1.0.0");
        using Meter instrumentTaggedMeter = new Meter(meterName, "1.0.0");

        Counter<long> baseline = baselineMeter.CreateCounter<long>("requests", "ms", "one");
        Counter<long> scopedCounter = scopedMeter.CreateCounter<long>("requests", "ms", "one");
        Counter<long> meterTaggedCounter = meterTagged.CreateCounter<long>("requests", "ms", "one");
        Counter<long> unitCounter = unitMeter.CreateCounter<long>("requests", "seconds", "one");
        Counter<long> descriptionCounter = descriptionMeter.CreateCounter<long>("requests", "ms", "two");
        Counter<int> typeCounter = typeMeter.CreateCounter<int>("requests", "ms", "one");
        Counter<long> instrumentTaggedCounter = instrumentTaggedMeter.CreateCounter<long>(
            "requests",
            "ms",
            "one",
            new[] { instrumentTag });

        MetricBudgetOptions options = new MetricBudgetOptions();
        options.ForInstrument(meterName, "requests", budget => budget.MaxObservedSeries = 10);

        using MetricBudgetSession session = MetricBudgetSession.Start(options);
        baseline.Add(1);
        scopedCounter.Add(1);
        meterTaggedCounter.Add(1);
        unitCounter.Add(1);
        descriptionCounter.Add(1);
        typeCounter.Add(1);
        instrumentTaggedCounter.Add(1);

        MetricBudgetReport report = session.Complete();

        Assert.Equal(MetricBudgetOutcome.Passed, report.Outcome);
        Assert.Equal(6, report.ObservedInstrumentCount);
        Assert.Equal(6, report.Rules[0].Instruments.Count);
        Assert.Equal(6, report.Rules[0].Instruments.Select(instrument => instrument.IdentityDiscriminator).Distinct().Count());
        Assert.Equal(7, report.TotalMeasurementsObserved);
        MetricBudgetInstrumentResult equivalent = report.Rules[0].Instruments.Single(instrument => instrument.MeasurementCount == 2);
        Assert.Equal(64, equivalent.IdentityDiscriminator.Length);
        Assert.Equal(2, equivalent.MeasurementCount);
    }

    [Fact]
    public void IdentityAdmissionLossAcrossStaticMetadataVariantsFailsClosedForFocusedObservation()
    {
        string meterName = TestNames.Meter(nameof(IdentityAdmissionLossAcrossStaticMetadataVariantsFailsClosedForFocusedObservation));
        KeyValuePair<string, object?> meterTag = new KeyValuePair<string, object?>("scope", "one");
        KeyValuePair<string, object?> instrumentTag = new KeyValuePair<string, object?>("stream", "one");

        using Meter baselineMeter = new Meter(meterName, "1.0.0");
        using Meter meterTagged = new Meter(new MeterOptions(meterName)
        {
            Version = "1.0.0",
            Tags = new[] { meterTag },
        });
        using Meter unitMeter = new Meter(meterName, "1.0.0");
        using Meter descriptionMeter = new Meter(meterName, "1.0.0");
        using Meter typeMeter = new Meter(meterName, "1.0.0");
        using Meter instrumentTaggedMeter = new Meter(meterName, "1.0.0");

        Counter<long> baseline = baselineMeter.CreateCounter<long>("requests", "ms", "one");
        Counter<long> meterTaggedCounter = meterTagged.CreateCounter<long>("requests", "ms", "one");
        Counter<long> unitCounter = unitMeter.CreateCounter<long>("requests", "seconds", "one");
        Counter<long> descriptionCounter = descriptionMeter.CreateCounter<long>("requests", "ms", "two");
        Counter<int> typeCounter = typeMeter.CreateCounter<int>("requests", "ms", "one");
        Counter<long> instrumentTaggedCounter = instrumentTaggedMeter.CreateCounter<long>(
            "requests",
            "ms",
            "one",
            new[] { instrumentTag });

        MetricBudgetOptions options = new MetricBudgetOptions
        {
            MaxTrackedInstrumentIdentities = 1,
        };
        options.ForInstrument(meterName, "requests", budget => budget.MaxObservedSeries = 1);

        using MetricBudgetSession session = MetricBudgetSession.Start(options);
        baseline.Add(1);
        meterTaggedCounter.Add(1);
        unitCounter.Add(1);
        descriptionCounter.Add(1);
        typeCounter.Add(1);
        instrumentTaggedCounter.Add(1);

        MetricBudgetReport report = session.Complete();
        MetricBudgetInstrumentResult retained = Assert.Single(report.Rules[0].Instruments);

        Assert.Equal(MetricBudgetOutcome.ObservationIncomplete, report.Outcome);
        Assert.Equal(5, report.Safety.UntrackedInstrumentIdentities);
        Assert.True(retained.WasObserved);
        Assert.True(retained.InstrumentTrackingIncomplete);
        Assert.Throws<MetricBudgetAssertionException>(
            () => report.AssertInstrumentObserved(meterName, "requests"));
        Assert.Throws<MetricBudgetAssertionException>(
            () => report.AssertInstrumentObserved(meterName, "requests", retained.IdentityDiscriminator));
    }

    [Fact]
    public void StaticMetadataDoesNotReuseDeliveredTagSafetyBounds()
    {
        string meterName = TestNames.Meter(nameof(StaticMetadataDoesNotReuseDeliveredTagSafetyBounds));
        using Meter meter = new Meter(new MeterOptions(meterName)
        {
            Version = "1.0.0",
            Tags = new[]
            {
                new KeyValuePair<string, object?>("static-one", "value-one"),
                new KeyValuePair<string, object?>("static-two", "value-two"),
            },
        });
        Counter<long> counter = meter.CreateCounter<long>(
            "requests",
            unit: "milliseconds",
            description: "description",
            tags: new[] { new KeyValuePair<string, object?>("instrument-static", "instrument-value") });
        MetricBudgetOptions options = new MetricBudgetOptions
        {
            MaxTagCount = 1,
            MaxTagKeyLength = 1,
            MaxTagValueLength = 1,
        };
        options.ForInstrument(meterName, "requests", budget => budget.MaxObservedSeries = 1);

        using MetricBudgetSession session = MetricBudgetSession.Start(options);
        counter.Add(1, new KeyValuePair<string, object?>("x", "y"));
        MetricBudgetReport report = session.Complete();

        Assert.Equal(MetricBudgetOutcome.Passed, report.Outcome);
        Assert.Empty(report.Safety.StaticMetadataFailures);
    }

    [Fact]
    public void StaticMetadataBoundFailureNamesTheActualDimension()
    {
        string meterName = TestNames.Meter(nameof(StaticMetadataBoundFailureNamesTheActualDimension));
        using Meter meter = new Meter(new MeterOptions(meterName)
        {
            Version = "1.0.0",
            Tags = new[]
            {
                new KeyValuePair<string, object?>("one", "one"),
                new KeyValuePair<string, object?>("two", "two"),
            },
        });
        Counter<long> counter = meter.CreateCounter<long>("requests");
        MetricBudgetOptions options = new MetricBudgetOptions
        {
            MaxStaticMetadataTagCount = 1,
            MaxTagCount = 64,
            MaxTagKeyLength = 256,
            MaxTagValueLength = 256,
        };
        options.ForInstrument(meterName, "requests", budget => budget.MaxObservedSeries = 1);

        using MetricBudgetSession session = MetricBudgetSession.Start(options);
        counter.Add(1);
        MetricBudgetReport report = session.Complete();

        MetricBudgetStaticMetadataFailure failure = Assert.Single(report.Safety.StaticMetadataFailures);
        Assert.Equal(MetricBudgetStaticMetadataFailureKind.MeterTagCount, failure.Kind);
        Assert.Equal(1, failure.EffectiveLimit);
        Assert.Equal(MetricBudgetOutcome.ObservationIncomplete, report.Outcome);
        Assert.Contains("MaxStaticMetadataTagCount", report.ToDiagnosticString(), StringComparison.Ordinal);
        Assert.DoesNotContain("MaxInstrumentIdentityLength", report.ToDiagnosticString(), StringComparison.Ordinal);
    }

    [Fact]
    public void StaticMetadataKeyValueAndTextBoundsRemainIndependent()
    {
        string meterName = TestNames.Meter(nameof(StaticMetadataKeyValueAndTextBoundsRemainIndependent));
        using Meter meter = new Meter(new MeterOptions(meterName)
        {
            Version = "1.0.0",
            Tags = new[] { new KeyValuePair<string, object?>("meter-key", "meter-value") },
        });
        Counter<long> counter = meter.CreateCounter<long>(
            "requests",
            unit: "unit-value",
            description: "description-value",
            tags: new[] { new KeyValuePair<string, object?>("instrument-key", "instrument-value") });
        MetricBudgetOptions options = new MetricBudgetOptions
        {
            MaxStaticMetadataTagKeyLength = 3,
            MaxStaticMetadataTagValueLength = 3,
            MaxStaticMetadataTextLength = 3,
        };
        options.ForInstrument(meterName, "requests", budget => budget.MaxObservedSeries = 1);

        using MetricBudgetSession session = MetricBudgetSession.Start(options);
        counter.Add(1);
        MetricBudgetReport report = session.Complete();
        MetricBudgetStaticMetadataFailureKind[] kinds = report.Safety.StaticMetadataFailures
            .Select(failure => failure.Kind)
            .ToArray();

        Assert.Equal(MetricBudgetOutcome.ObservationIncomplete, report.Outcome);
        Assert.Contains(MetricBudgetStaticMetadataFailureKind.TextLength, kinds);
        Assert.Contains(MetricBudgetStaticMetadataFailureKind.MeterTagKeyLength, kinds);
        Assert.Contains(MetricBudgetStaticMetadataFailureKind.MeterTagValueLength, kinds);
        Assert.Contains(MetricBudgetStaticMetadataFailureKind.InstrumentTagKeyLength, kinds);
        Assert.Contains(MetricBudgetStaticMetadataFailureKind.InstrumentTagValueLength, kinds);
        Assert.DoesNotContain("meter-value", report.ToDiagnosticString(), StringComparison.Ordinal);
        Assert.DoesNotContain("instrument-value", report.ToDiagnosticString(), StringComparison.Ordinal);
    }

    [Fact]
    public void StaticMetadataHardCeilingIsAppliedWhenCallerRaisesOption()
    {
        string meterName = TestNames.Meter(nameof(StaticMetadataHardCeilingIsAppliedWhenCallerRaisesOption));
        using Meter meter = new Meter(new MeterOptions(meterName)
        {
            Version = "1.0.0",
            Tags = Enumerable.Range(0, 257)
                .Select(index => new KeyValuePair<string, object?>("tag-" + index, index))
                .ToArray(),
        });
        Counter<long> counter = meter.CreateCounter<long>("requests");
        MetricBudgetOptions options = new MetricBudgetOptions
        {
            MaxStaticMetadataTagCount = 1000,
        };
        options.ForInstrument(meterName, "requests", budget => budget.MaxObservedSeries = 1);

        using MetricBudgetSession session = MetricBudgetSession.Start(options);
        counter.Add(1);
        MetricBudgetReport report = session.Complete();
        MetricBudgetStaticMetadataFailure failure = Assert.Single(report.Safety.StaticMetadataFailures);

        Assert.Equal(MetricBudgetStaticMetadataFailureKind.MeterTagCount, failure.Kind);
        Assert.Equal(256, failure.EffectiveLimit);
        Assert.Equal(MetricBudgetOutcome.ObservationIncomplete, report.Outcome);
    }

    [Fact]
    public void FocusedAssertionCanSelectCompleteIdentityDiscriminator()
    {
        string meterName = TestNames.Meter(nameof(FocusedAssertionCanSelectCompleteIdentityDiscriminator));
        using Meter firstMeter = new Meter(new MeterOptions(meterName)
        {
            Version = "1.0.0",
            Tags = new[] { new KeyValuePair<string, object?>("stream", "first-secret") },
        });
        using Meter secondMeter = new Meter(new MeterOptions(meterName)
        {
            Version = "1.0.0",
            Tags = new[] { new KeyValuePair<string, object?>("stream", "second-secret") },
        });
        Counter<long> first = firstMeter.CreateCounter<long>("requests");
        Counter<long> second = secondMeter.CreateCounter<long>("requests");
        MetricBudgetOptions options = new MetricBudgetOptions();
        options.ForInstrument(meterName, "requests", budget => budget.MaxObservedSeries = 1);

        using MetricBudgetSession session = MetricBudgetSession.Start(options);
        first.Add(1, new KeyValuePair<string, object?>("route", "/first"));
        second.Add(1, new KeyValuePair<string, object?>("route", "/second-a"));
        second.Add(1, new KeyValuePair<string, object?>("route", "/second-b"));
        MetricBudgetReport report = session.Complete();
        IReadOnlyList<MetricBudgetInstrumentResult> instruments = report.Rules[0].Instruments;
        Assert.Equal(2, instruments.Count);
        Assert.Equal(1, report.Violations.Count(violation => violation.Kind == MetricBudgetViolationKind.ObservedSeriesBudgetExceeded));
        Assert.All(
            report.Violations.Where(violation => violation.Kind == MetricBudgetViolationKind.ObservedSeriesBudgetExceeded),
            violation => Assert.NotNull(violation.IdentityDiscriminator));

        MetricBudgetInstrumentResult firstResult = instruments.Single(instrument => instrument.ObservedSeriesCount == 1);
        MetricBudgetInstrumentResult secondResult = instruments.Single(instrument => instrument.ObservedSeriesCount == 2);
        Assert.Throws<MetricBudgetAssertionException>(
            () => report.AssertObservedSeriesAtMost(meterName, "requests", 1));
        _ = report.AssertObservedSeriesAtMost(
            meterName,
            "requests",
            firstResult.IdentityDiscriminator,
            1);
        MetricBudgetAssertionException exception = Assert.Throws<MetricBudgetAssertionException>(
            () => report.AssertObservedSeriesAtMost(
                meterName,
                "requests",
                secondResult.IdentityDiscriminator,
                1));
        Assert.Contains(secondResult.IdentityDiscriminator, exception.Message, StringComparison.Ordinal);
        Assert.Contains(firstResult.IdentityDiscriminator, report.ToDiagnosticString(), StringComparison.Ordinal);
        Assert.Contains(secondResult.IdentityDiscriminator, report.ToDiagnosticString(), StringComparison.Ordinal);
        Assert.DoesNotContain("first-secret", report.ToDiagnosticString(), StringComparison.Ordinal);
        Assert.DoesNotContain("second-secret", report.ToDiagnosticString(), StringComparison.Ordinal);
    }

    [Fact]
    public void UnsupportedStaticMetadataIsRejectedWithoutNameOnlyMerge()
    {
        string meterName = TestNames.Meter(nameof(UnsupportedStaticMetadataIsRejectedWithoutNameOnlyMerge));
        using Meter meter = new Meter(new MeterOptions(meterName)
        {
            Version = "1.0.0",
            Tags = new[] { new KeyValuePair<string, object?>("scope", new ThrowingValue()) },
        });
        Counter<long> counter = meter.CreateCounter<long>("requests");
        MetricBudgetOptions options = new MetricBudgetOptions();
        options.ForInstrument(meterName, "requests", budget => budget.MaxObservedSeries = 1);

        using MetricBudgetSession session = MetricBudgetSession.Start(options);
        counter.Add(1);
        MetricBudgetReport report = session.Complete();

        Assert.Equal(MetricBudgetOutcome.ObservationIncomplete, report.Outcome);
        Assert.Empty(report.Rules[0].Instruments);
        Assert.Equal(0, report.ObservedInstrumentCount);
        MetricBudgetStaticMetadataFailure failure = Assert.Single(report.Safety.StaticMetadataFailures);
        Assert.Equal(MetricBudgetStaticMetadataFailureKind.MeterTagValue, failure.Kind);
        Assert.Null(failure.EffectiveLimit);
    }

    [Fact]
    public void OverlongStaticDescriptionIsRejectedBeforeAdmission()
    {
        string meterName = TestNames.Meter(nameof(OverlongStaticDescriptionIsRejectedBeforeAdmission));
        using Meter meter = new Meter(meterName, "1.0.0");
        Counter<long> counter = meter.CreateCounter<long>("requests", description: new string('d', 65));
        MetricBudgetOptions options = new MetricBudgetOptions { MaxStaticMetadataTextLength = 64 };
        options.ForInstrument(meterName, "requests", budget => budget.MaxObservedSeries = 1);

        using MetricBudgetSession session = MetricBudgetSession.Start(options);
        counter.Add(1);
        MetricBudgetReport report = session.Complete();

        Assert.Equal(MetricBudgetOutcome.ObservationIncomplete, report.Outcome);
        Assert.Empty(report.Rules[0].Instruments);
        MetricBudgetStaticMetadataFailure failure = Assert.Single(report.Safety.StaticMetadataFailures);
        Assert.Equal(MetricBudgetStaticMetadataFailureKind.TextLength, failure.Kind);
        Assert.Equal(64, failure.EffectiveLimit);
    }

    [Fact]
    public async Task AdmittedMeasurementCommitsBeforeCompleteReturns()
    {
        string meterName = TestNames.Meter(nameof(AdmittedMeasurementCommitsBeforeCompleteReturns));
        using Meter meter = new Meter(meterName, "1.0.0");
        Counter<long> counter = meter.CreateCounter<long>("requests");
        MetricBudgetOptions options = new MetricBudgetOptions();
        options.ForInstrument(meterName, "requests", budget => budget.MaxObservedSeries = 1);
        using MetricBudgetSession session = MetricBudgetSession.Start(options);

        using ManualResetEventSlim commitEntered = new ManualResetEventSlim();
        using ManualResetEventSlim releaseCommit = new ManualResetEventSlim();
        int entered = 0;
        MetricBudgetState.BeforeMeasurementCommitForTesting = () =>
        {
            if (Interlocked.Exchange(ref entered, 1) == 0)
            {
                commitEntered.Set();
                releaseCommit.Wait();
            }
        };

        try
        {
            Task measurement = Task.Run(() => counter.Add(1));
            Assert.True(commitEntered.Wait(TimeSpan.FromSeconds(5)));
            Task<MetricBudgetReport> completion = Task.Run(session.Complete);

            Task completedOrTimedOut = await Task.WhenAny(completion, Task.Delay(100));
            Assert.NotSame(completion, completedOrTimedOut);
            releaseCommit.Set();

            await measurement;
            MetricBudgetReport report = await completion;
            Assert.Equal(1, report.TotalMeasurementsObserved);
            Assert.Equal(1, report.ObservedSeriesCount);
            Assert.Equal(MetricBudgetOutcome.Passed, report.Outcome);
        }
        finally
        {
            releaseCommit.Set();
            MetricBudgetState.BeforeMeasurementCommitForTesting = null;
        }
    }

    [Fact]
    public async Task AdmittedUnsupportedMeasurementCommitsBeforeCompleteReturns()
    {
        string meterName = TestNames.Meter(nameof(AdmittedUnsupportedMeasurementCommitsBeforeCompleteReturns));
        using Meter meter = new Meter(meterName, "1.0.0");
        Counter<long> counter = meter.CreateCounter<long>("requests");
        MetricBudgetOptions options = new MetricBudgetOptions { MaxTagCount = 1 };
        options.ForInstrument(meterName, "requests", budget => budget.MaxObservedSeries = 1);
        using MetricBudgetSession session = MetricBudgetSession.Start(options);

        using ManualResetEventSlim commitEntered = new ManualResetEventSlim();
        using ManualResetEventSlim releaseCommit = new ManualResetEventSlim();
        int entered = 0;
        MetricBudgetState.BeforeMeasurementCommitForTesting = () =>
        {
            if (Interlocked.Exchange(ref entered, 1) == 0)
            {
                commitEntered.Set();
                releaseCommit.Wait();
            }
        };

        try
        {
            Task measurement = Task.Run(
                () => counter.Add(
                    1,
                    new KeyValuePair<string, object?>("first", 1),
                    new KeyValuePair<string, object?>("second", 2)));
            Assert.True(commitEntered.Wait(TimeSpan.FromSeconds(5)));
            Task<MetricBudgetReport> completion = Task.Run(session.Complete);

            Task completedOrTimedOut = await Task.WhenAny(completion, Task.Delay(100));
            Assert.NotSame(completion, completedOrTimedOut);
            releaseCommit.Set();

            await measurement;
            MetricBudgetReport report = await completion;
            Assert.Equal(1, report.TotalMeasurementsObserved);
            Assert.Equal(MetricBudgetOutcome.ObservationIncomplete, report.Outcome);
            Assert.True(report.Safety.TagSetTrackingIncomplete);
        }
        finally
        {
            releaseCommit.Set();
            MetricBudgetState.BeforeMeasurementCommitForTesting = null;
        }
    }

    [Fact]
    public async Task RepeatedAdmittedMeasurementCommitsBeforeCompleteReturns()
    {
        string meterName = TestNames.Meter(nameof(RepeatedAdmittedMeasurementCommitsBeforeCompleteReturns));
        using Meter meter = new Meter(meterName, "1.0.0");
        Counter<long> counter = meter.CreateCounter<long>("requests");
        MetricBudgetOptions options = new MetricBudgetOptions();
        options.ForInstrument(meterName, "requests", budget => budget.MaxObservedSeries = 1);
        using MetricBudgetSession session = MetricBudgetSession.Start(options);

        using ManualResetEventSlim commitEntered = new ManualResetEventSlim();
        using ManualResetEventSlim releaseCommit = new ManualResetEventSlim();
        int entered = 0;
        MetricBudgetState.BeforeMeasurementCommitForTesting = () =>
        {
            if (Interlocked.Exchange(ref entered, 1) == 0)
            {
                commitEntered.Set();
                releaseCommit.Wait();
            }
        };

        try
        {
            Task first = Task.Run(() => counter.Add(1));
            Assert.True(commitEntered.Wait(TimeSpan.FromSeconds(5)));
            Task second = Task.Run(() => counter.Add(1));
            await second;
            Task<MetricBudgetReport> completion = Task.Run(session.Complete);

            Task completedOrTimedOut = await Task.WhenAny(completion, Task.Delay(100));
            Assert.NotSame(completion, completedOrTimedOut);
            releaseCommit.Set();

            await first;
            MetricBudgetReport report = await completion;
            Assert.Equal(2, report.TotalMeasurementsObserved);
            Assert.Equal(2, report.Rules[0].Instruments[0].MeasurementCount);
            Assert.Equal(MetricBudgetOutcome.Passed, report.Outcome);
        }
        finally
        {
            releaseCommit.Set();
            MetricBudgetState.BeforeMeasurementCommitForTesting = null;
        }
    }

    [Fact]
    public async Task AdmittedCallbacksCrossBudgetBoundaryBeforeCompleteReturns()
    {
        string meterName = TestNames.Meter(nameof(AdmittedCallbacksCrossBudgetBoundaryBeforeCompleteReturns));
        using Meter meter = new Meter(meterName, "1.0.0");
        Counter<long> counter = meter.CreateCounter<long>("requests");
        MetricBudgetOptions options = new MetricBudgetOptions { MaxTrackedSeries = 2 };
        options.ForInstrument(meterName, "requests", budget => budget.MaxObservedSeries = 1);
        using MetricBudgetSession session = MetricBudgetSession.Start(options);

        using ManualResetEventSlim commitEntered = new ManualResetEventSlim();
        using ManualResetEventSlim releaseCommit = new ManualResetEventSlim();
        int entered = 0;
        MetricBudgetState.BeforeMeasurementCommitForTesting = () =>
        {
            if (Interlocked.Exchange(ref entered, 1) == 0)
            {
                commitEntered.Set();
                releaseCommit.Wait();
            }
        };

        try
        {
            Task first = Task.Run(
                () => counter.Add(1, new KeyValuePair<string, object?>("route", "first")));
            Assert.True(commitEntered.Wait(TimeSpan.FromSeconds(5)));
            Task second = Task.Run(
                () => counter.Add(1, new KeyValuePair<string, object?>("route", "second")));
            await second;
            Task<MetricBudgetReport> completion = Task.Run(session.Complete);

            Task completedOrTimedOut = await Task.WhenAny(completion, Task.Delay(100));
            Assert.NotSame(completion, completedOrTimedOut);
            releaseCommit.Set();

            await first;
            MetricBudgetReport report = await completion;
            Assert.Equal(2, report.TotalMeasurementsObserved);
            Assert.Equal(2, report.ObservedSeriesCount);
            Assert.Equal(MetricBudgetOutcome.Violation, report.Outcome);
        }
        finally
        {
            releaseCommit.Set();
            MetricBudgetState.BeforeMeasurementCommitForTesting = null;
        }
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
            () => report.AssertInstrumentObserved(meterName, "requests"));
        Assert.Throws<MetricBudgetAssertionException>(
            () => report.AssertInstrumentObserved(meterName, "requests", instrument.IdentityDiscriminator));
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
        MetricBudgetInstrumentResult retainedResult = Assert.Single(report.Rules[0].Instruments);
        Assert.Throws<MetricBudgetAssertionException>(
            () => report.AssertInstrumentObserved(meterName, "requests"));
        Assert.Throws<MetricBudgetAssertionException>(
            () => report.AssertInstrumentObserved(meterName, "requests", retainedResult.IdentityDiscriminator));
        Assert.Throws<MetricBudgetAssertionException>(
            () => report.AssertObservedSeriesAtMost(meterName, "requests", 1));
        Assert.Throws<MetricBudgetAssertionException>(
            () => report.AssertTagDistinctValuesAtMost(meterName, "requests", "tenant", 1));
    }

    [Fact]
    public void IdentityAdmissionLossMakesObservedAssertionsFailClosedForCompleteIdentity()
    {
        string meterName = TestNames.Meter(nameof(IdentityAdmissionLossMakesObservedAssertionsFailClosedForCompleteIdentity));
        using Meter firstMeter = new Meter(meterName, "1.0.0");
        using Meter secondMeter = new Meter(meterName, "1.0.0");
        Counter<long> retained = firstMeter.CreateCounter<long>("requests", unit: "milliseconds", description: "first");
        Counter<long> rejected = secondMeter.CreateCounter<long>("requests", unit: "seconds", description: "second");
        MetricBudgetOptions options = new MetricBudgetOptions
        {
            MaxTrackedInstrumentIdentities = 1,
        };
        options.ForInstrument(meterName, "requests", budget => budget.MaxObservedSeries = 1);

        using MetricBudgetSession session = MetricBudgetSession.Start(options);
        retained.Add(1);
        rejected.Add(1);

        MetricBudgetReport report = session.Complete();
        MetricBudgetInstrumentResult retainedResult = Assert.Single(report.Rules[0].Instruments);

        Assert.Equal(MetricBudgetOutcome.ObservationIncomplete, report.Outcome);
        Assert.True(retainedResult.WasObserved);
        Assert.True(retainedResult.InstrumentTrackingIncomplete);
        Assert.Throws<MetricBudgetAssertionException>(
            () => report.AssertInstrumentObserved(meterName, "requests"));
        Assert.Throws<MetricBudgetAssertionException>(
            () => report.AssertInstrumentObserved(meterName, "requests", retainedResult.IdentityDiscriminator));
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
            () => report.AssertInstrumentObserved(meterName, instrumentName));
        Assert.Throws<MetricBudgetAssertionException>(
            () => report.AssertInstrumentObserved(meterName, instrumentName, instrument.IdentityDiscriminator));
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
            () => report.AssertInstrumentObserved(meterName, instrumentName));
        Assert.Throws<MetricBudgetAssertionException>(
            () => report.AssertInstrumentObserved(meterName, instrumentName, instrument.IdentityDiscriminator));
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
            () => report.AssertInstrumentObserved(meterName, instrumentName));
        Assert.Throws<MetricBudgetAssertionException>(
            () => report.AssertInstrumentObserved(meterName, instrumentName, instrument.IdentityDiscriminator));
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

        Assert.Equal(MetricBudgetOutcome.InvalidConfiguration, report.Outcome);
        Assert.True(report.Safety.ConflictTrackingIncomplete);
        Assert.Contains(report.Violations, violation => violation.Kind == MetricBudgetViolationKind.ConfigurationInvalid);
    }

    [Fact]
    public void OverlongOverlappingSelectorRemainsInvalidConfiguration()
    {
        string meterName = TestNames.Meter(nameof(OverlongOverlappingSelectorRemainsInvalidConfiguration));
        using Meter meter = new Meter(meterName, "1.0.0");
        Counter<long> counter = meter.CreateCounter<long>("requests");
        MetricBudgetOptions options = new MetricBudgetOptions
        {
            MaxInstrumentIdentityLength = 4,
        };
        options.ForMeter(meterName, budget => budget.MaxObservedSeries = 10);
        options.ForInstrument(meterName, "requests", budget => budget.MaxObservedSeries = 10);

        using MetricBudgetSession session = MetricBudgetSession.Start(options);
        counter.Add(1);
        MetricBudgetReport report = session.Complete();

        Assert.Equal(MetricBudgetOutcome.InvalidConfiguration, report.Outcome);
        Assert.Contains(report.Violations, violation => violation.Kind == MetricBudgetViolationKind.ConfigurationInvalid);
        Assert.All(report.Rules, rule => Assert.False(rule.IsWithinBudget));
        Assert.True(report.Safety.InstrumentIdentityLengthTrackingIncomplete);
    }

    [Fact]
    public void ManyMatchingRulesRemainInvalidWhenTheirIndexesExceedTheConflictBound()
    {
        string meterName = TestNames.Meter(nameof(ManyMatchingRulesRemainInvalidWhenTheirIndexesExceedTheConflictBound));
        using Meter meter = new Meter(meterName, "1.0.0");
        Counter<long> counter = meter.CreateCounter<long>("requests");
        MetricBudgetOptions options = new MetricBudgetOptions
        {
            MaxTrackedConflicts = 2,
        };
        options.ForMeter(meterName, budget => budget.MaxObservedSeries = 10);
        options.ForMeter(meterName, budget => budget.MaxObservedSeries = 10);
        options.ForInstrument(meterName, "requests", budget => budget.MaxObservedSeries = 10);

        using MetricBudgetSession session = MetricBudgetSession.Start(options);
        counter.Add(1);
        MetricBudgetReport report = session.Complete();

        Assert.Equal(MetricBudgetOutcome.InvalidConfiguration, report.Outcome);
        Assert.True(report.Safety.ConflictTrackingIncomplete);
        Assert.Contains(report.Violations, violation => violation.Kind == MetricBudgetViolationKind.ConfigurationInvalid);
        Assert.All(report.Rules, rule => Assert.False(rule.IsWithinBudget));
    }

    [Fact]
    public void ExhaustedConflictCollectionRetainsKnownInvalidConfiguration()
    {
        string meterName = TestNames.Meter(nameof(ExhaustedConflictCollectionRetainsKnownInvalidConfiguration));
        using Meter meter = new Meter(meterName, "1.0.0");
        Counter<long> counter = meter.CreateCounter<long>("requests");
        Histogram<long> histogram = meter.CreateHistogram<long>("requests");
        MetricBudgetOptions options = new MetricBudgetOptions
        {
            MaxTrackedConflicts = 1,
        };
        options.ForMeter(meterName, budget => budget.MaxObservedSeries = 10);
        options.ForInstrument(meterName, "requests", budget => budget.MaxObservedSeries = 10);

        using MetricBudgetSession session = MetricBudgetSession.Start(options);
        counter.Add(1);
        histogram.Record(1);
        MetricBudgetReport report = session.Complete();

        Assert.Equal(MetricBudgetOutcome.InvalidConfiguration, report.Outcome);
        Assert.True(report.Safety.ConflictTrackingIncomplete);
        Assert.True(report.Safety.UntrackedConflicts > 0);
        Assert.Contains(report.Violations, violation => violation.Kind == MetricBudgetViolationKind.ConfigurationInvalid);
        Assert.All(report.Rules, rule => Assert.False(rule.IsWithinBudget));
    }

    [Fact]
    public void ConflictDetectedForInstrumentPublishedAfterSessionStartRemainsInvalid()
    {
        string meterName = TestNames.Meter(nameof(ConflictDetectedForInstrumentPublishedAfterSessionStartRemainsInvalid));
        MetricBudgetOptions options = new MetricBudgetOptions
        {
            MaxTrackedConflicts = 1,
        };
        options.ForMeter(meterName, budget => budget.MaxObservedSeries = 10);
        options.ForInstrument(meterName, "requests", budget => budget.MaxObservedSeries = 10);

        using MetricBudgetSession session = MetricBudgetSession.Start(options);
        using Meter meter = new Meter(meterName, "1.0.0");
        Counter<long> counter = meter.CreateCounter<long>("requests");
        counter.Add(1);
        MetricBudgetReport report = session.Complete();

        Assert.Equal(MetricBudgetOutcome.InvalidConfiguration, report.Outcome);
        Assert.True(report.Safety.ConflictTrackingIncomplete);
        Assert.Contains(report.Violations, violation => violation.Kind == MetricBudgetViolationKind.ConfigurationInvalid);
        Assert.All(report.Rules, rule => Assert.False(rule.IsWithinBudget));
    }

    [Fact]
    public void KnownConflictTakesPrecedenceOverIndependentBudgetViolation()
    {
        string violatingMeterName = TestNames.Meter(nameof(KnownConflictTakesPrecedenceOverIndependentBudgetViolation) + ".violating");
        string conflictingMeterName = TestNames.Meter(nameof(KnownConflictTakesPrecedenceOverIndependentBudgetViolation) + ".conflicting");
        using Meter violatingMeter = new Meter(violatingMeterName, "1.0.0");
        using Meter conflictingMeter = new Meter(conflictingMeterName, "1.0.0");
        Counter<long> violating = violatingMeter.CreateCounter<long>("requests");
        Counter<long> conflicting = conflictingMeter.CreateCounter<long>("requests");

        MetricBudgetOptions options = new MetricBudgetOptions
        {
            MaxTrackedConflicts = 1,
        };
        options.ForInstrument(violatingMeterName, "requests", budget => budget.MaxObservedSeries = 1);
        options.ForMeter(conflictingMeterName, budget => budget.MaxObservedSeries = 10);
        options.ForInstrument(conflictingMeterName, "requests", budget => budget.MaxObservedSeries = 10);

        using MetricBudgetSession session = MetricBudgetSession.Start(options);
        violating.Add(1, new KeyValuePair<string, object?>("route", "/a"));
        violating.Add(1, new KeyValuePair<string, object?>("route", "/b"));
        conflicting.Add(1);
        MetricBudgetReport report = session.Complete();

        Assert.Equal(MetricBudgetOutcome.InvalidConfiguration, report.Outcome);
        Assert.Contains(report.Violations, violation => violation.Kind == MetricBudgetViolationKind.ObservedSeriesBudgetExceeded);
        Assert.Contains(report.Violations, violation => violation.Kind == MetricBudgetViolationKind.ConfigurationInvalid);
        Assert.False(report.Rules[0].IsWithinBudget);
        Assert.False(report.Rules[1].IsWithinBudget);
        Assert.False(report.Rules[2].IsWithinBudget);
    }

    [Fact]
    public void DateTimeIdentityPreservesTicksAndKindWithoutTimezoneNormalization()
    {
        DateTime springForwardGap = new DateTime(2010, 3, 14, 2, 30, 0, DateTimeKind.Local);
        DateTime afterSpringForwardGap = new DateTime(2010, 3, 14, 3, 30, 0, DateTimeKind.Local);
        DateTimeOffset fallBackDaylightOccurrence = new DateTimeOffset(2010, 11, 7, 1, 30, 0, TimeSpan.FromHours(-7));
        DateTimeOffset fallBackStandardOccurrence = new DateTimeOffset(2010, 11, 7, 1, 30, 0, TimeSpan.FromHours(-8));
        DateTime fallBackFirstOccurrence = DateTime.SpecifyKind(fallBackDaylightOccurrence.DateTime, DateTimeKind.Local);
        DateTime fallBackSecondOccurrence = DateTime.SpecifyKind(fallBackStandardOccurrence.DateTime, DateTimeKind.Local);
        DateTime adjacent = new DateTime(DateTime.MaxValue.Ticks - 1, DateTimeKind.Unspecified);
        DateTime minimum = new DateTime(DateTime.MinValue.Ticks, DateTimeKind.Unspecified);
        DateTime maximum = new DateTime(DateTime.MaxValue.Ticks, DateTimeKind.Unspecified);
        DateTime sameTicksLocal = new DateTime(123456789, DateTimeKind.Local);
        DateTime sameTicksUtc = new DateTime(123456789, DateTimeKind.Utc);
        DateTime sameTicksUnspecified = new DateTime(123456789, DateTimeKind.Unspecified);

        Assert.Equal(
            "System.DateTime:kind=Local:ticks=" + springForwardGap.Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture),
            TagIdentity.DescribeValue(springForwardGap, 256));
        Assert.Equal(
            "System.DateTime:kind=Local:ticks=" + afterSpringForwardGap.Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture),
            TagIdentity.DescribeValue(afterSpringForwardGap, 256));
        Assert.NotEqual(springForwardGap.Ticks, afterSpringForwardGap.Ticks);
        Assert.NotEqual(
            TagIdentity.DescribeValue(springForwardGap, 256),
            TagIdentity.DescribeValue(afterSpringForwardGap, 256));
        Assert.Equal(
            TagIdentity.DescribeValue(fallBackFirstOccurrence, 256),
            TagIdentity.DescribeValue(fallBackSecondOccurrence, 256));
        Assert.NotEqual(
            TagIdentity.DescribeValue(fallBackDaylightOccurrence, 256),
            TagIdentity.DescribeValue(fallBackStandardOccurrence, 256));
        Assert.NotEqual(
            TagIdentity.DescribeValue(sameTicksUtc, 256),
            TagIdentity.DescribeValue(sameTicksUnspecified, 256));
        Assert.NotEqual(
            TagIdentity.DescribeValue(sameTicksLocal, 256),
            TagIdentity.DescribeValue(sameTicksUtc, 256));
        Assert.NotEqual(
            TagIdentity.DescribeValue(sameTicksLocal, 256),
            TagIdentity.DescribeValue(sameTicksUnspecified, 256));
        Assert.NotEqual(TagIdentity.DescribeValue(adjacent, 256), TagIdentity.DescribeValue(maximum, 256));
        Assert.NotEqual(TagIdentity.DescribeValue(minimum, 256), TagIdentity.DescribeValue(maximum, 256));
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
        Assert.Contains("IdentityDiscriminator", exception.Message, StringComparison.Ordinal);
        Assert.Contains("identityDiscriminator", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("full meter version and instrument kind", exception.Message, StringComparison.Ordinal);
        Assert.All(
            report.Rules[0].Instruments,
            instrument => Assert.Contains(instrument.IdentityDiscriminator, exception.Message, StringComparison.Ordinal));
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
        Assert.Contains("IdentityDiscriminator", exception.Message, StringComparison.Ordinal);
        Assert.Contains("identityDiscriminator", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("full meter version and instrument kind", exception.Message, StringComparison.Ordinal);
        Assert.All(
            report.Rules[0].Instruments,
            instrument => Assert.Contains(instrument.IdentityDiscriminator, exception.Message, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("unit")]
    [InlineData("description")]
    [InlineData("measurement type")]
    [InlineData("meter tags")]
    [InlineData("instrument tags")]
    public void FocusedAssertionListsDiscriminatorForEachStaticMetadataDimension(string dimension)
    {
        string meterName = TestNames.Meter(nameof(FocusedAssertionListsDiscriminatorForEachStaticMetadataDimension) + dimension);
        Meter firstMeter;
        Meter secondMeter;
        Action firstMeasurement;
        Action secondMeasurement;

        switch (dimension)
        {
            case "meter tags":
                firstMeter = new Meter(new MeterOptions(meterName)
                {
                    Version = "1.0.0",
                    Tags = new[] { new KeyValuePair<string, object?>("stream", "first") },
                });
                secondMeter = new Meter(new MeterOptions(meterName)
                {
                    Version = "1.0.0",
                    Tags = new[] { new KeyValuePair<string, object?>("stream", "second") },
                });
                Counter<long> firstMeterTagCounter = firstMeter.CreateCounter<long>("requests");
                Counter<long> secondMeterTagCounter = secondMeter.CreateCounter<long>("requests");
                firstMeasurement = () => firstMeterTagCounter.Add(1);
                secondMeasurement = () => secondMeterTagCounter.Add(1);
                break;
            default:
                firstMeter = new Meter(meterName, "1.0.0");
                secondMeter = firstMeter;
                switch (dimension)
                {
                    case "unit":
                        Counter<long> firstUnitCounter = firstMeter.CreateCounter<long>("requests", unit: "items");
                        Counter<long> secondUnitCounter = firstMeter.CreateCounter<long>("requests", unit: "seconds");
                        firstMeasurement = () => firstUnitCounter.Add(1);
                        secondMeasurement = () => secondUnitCounter.Add(1);
                        break;
                    case "description":
                        Counter<long> firstDescriptionCounter = firstMeter.CreateCounter<long>(
                            "requests",
                            description: "first");
                        Counter<long> secondDescriptionCounter = firstMeter.CreateCounter<long>(
                            "requests",
                            description: "second");
                        firstMeasurement = () => firstDescriptionCounter.Add(1);
                        secondMeasurement = () => secondDescriptionCounter.Add(1);
                        break;
                    case "measurement type":
                        Counter<long> longCounter = firstMeter.CreateCounter<long>("requests");
                        Counter<int> intCounter = firstMeter.CreateCounter<int>("requests");
                        firstMeasurement = () => longCounter.Add(1);
                        secondMeasurement = () => intCounter.Add(1);
                        break;
                    case "instrument tags":
                        Counter<long> firstInstrumentTagCounter = firstMeter.CreateCounter<long>(
                            "requests",
                            unit: null,
                            description: null,
                            tags: new[] { new KeyValuePair<string, object?>("stream", "first") });
                        Counter<long> secondInstrumentTagCounter = firstMeter.CreateCounter<long>(
                            "requests",
                            unit: null,
                            description: null,
                            tags: new[] { new KeyValuePair<string, object?>("stream", "second") });
                        firstMeasurement = () => firstInstrumentTagCounter.Add(1);
                        secondMeasurement = () => secondInstrumentTagCounter.Add(1);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(dimension), dimension, "Unknown identity dimension.");
                }

                break;
        }

        using (firstMeter)
        using (secondMeter == firstMeter ? null : secondMeter)
        {
            MetricBudgetOptions options = new MetricBudgetOptions();
            options.ForInstrument(meterName, "requests", budget => budget.MaxObservedSeries = 10);
            using MetricBudgetSession session = MetricBudgetSession.Start(options);
            firstMeasurement();
            secondMeasurement();
            MetricBudgetReport report = session.Complete();

            Assert.Equal(2, report.Rules[0].Instruments.Count);
            MetricBudgetAssertionException exception = Assert.Throws<MetricBudgetAssertionException>(
                () => report.AssertObservedSeriesAtMost(meterName, "requests", 1));
            Assert.Contains("ambiguous", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("IdentityDiscriminator", exception.Message, StringComparison.Ordinal);
            Assert.Contains("identityDiscriminator", exception.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("full meter version and instrument kind", exception.Message, StringComparison.Ordinal);
            Assert.All(
                report.Rules[0].Instruments,
                instrument => Assert.Contains(instrument.IdentityDiscriminator, exception.Message, StringComparison.Ordinal));
        }
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
