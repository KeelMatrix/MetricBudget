// Copyright (c) KeelMatrix

using System.Diagnostics.Metrics;

namespace KeelMatrix.MetricBudget.Tests;

/// <summary>
/// Per-tag distinct-value budgets, composite accounting, and tag edge cases.
/// </summary>
public sealed class TagBudgetTests
{
    [Fact]
    public void PassesWhenTagValuesStayWithinBudget()
    {
        string meterName = TestNames.Meter(nameof(PassesWhenTagValuesStayWithinBudget));
        using Meter meter = new Meter(meterName, "1.0.0");
        Counter<long> counter = meter.CreateCounter<long>("requests");

        MetricBudgetOptions options = new MetricBudgetOptions()
            .ForInstrument(meterName, "requests", budget =>
            {
                budget.MaxObservedSeries = 4;
                budget.Tag("route").MaxDistinctValues = 2;
            });

        using MetricBudgetSession session = MetricBudgetSession.Start(options);
        for (int i = 0; i < 4; i++)
        {
            counter.Add(1, new KeyValuePair<string, object?>("route", i % 2 == 0 ? "/a" : "/b"));
        }

        MetricBudgetReport report = session.Complete();

        Assert.Equal(MetricBudgetOutcome.Passed, report.Outcome);
        MetricBudgetInstrumentResult instrument = report.Rules[0].Instruments[0];
        MetricBudgetTagResult tag = Assert.Single(instrument.Tags);
        Assert.Equal("route", tag.Key);
        Assert.Equal(2, tag.ObservedDistinctValueCount);
        Assert.Equal(2, tag.ConfiguredMaxDistinctValues);
        Assert.True(tag.IsConfigured);
        Assert.True(tag.WasObserved);
        Assert.True(tag.IsWithinBudget);
    }

    [Fact]
    public void FailsWhenTagDistinctValuesExceedBudget()
    {
        string meterName = TestNames.Meter(nameof(FailsWhenTagDistinctValuesExceedBudget));
        using Meter meter = new Meter(meterName, "1.0.0");
        Counter<long> counter = meter.CreateCounter<long>("requests");

        MetricBudgetOptions options = new MetricBudgetOptions()
            .ForInstrument(meterName, "requests", budget =>
            {
                budget.MaxObservedSeries = 100;
                budget.Tag("server.address").MaxDistinctValues = 2;
            });

        using MetricBudgetSession session = MetricBudgetSession.Start(options);
        for (int i = 0; i < 5; i++)
        {
            counter.Add(1, new KeyValuePair<string, object?>("server.address", "host-" + i));
        }

        MetricBudgetReport report = session.Complete();

        Assert.Equal(MetricBudgetOutcome.Violation, report.Outcome);

        MetricBudgetTagResult tag = Assert.Single(report.Rules[0].Instruments[0].Tags);
        Assert.Equal(5, tag.ObservedDistinctValueCount);
        Assert.Equal(2, tag.ConfiguredMaxDistinctValues);
        Assert.False(tag.IsWithinBudget);

        MetricBudgetViolation violation = report.Violations
            .Single(candidate => candidate.Kind == MetricBudgetViolationKind.TagDistinctValuesBudgetExceeded);
        Assert.Equal("server.address", violation.TagKey);
        Assert.Equal(5, violation.ObservedCount);
        Assert.Equal(2, violation.ConfiguredLimit);
    }

    [Fact]
    public void ConfiguredTagKeyThatTheWorkloadNeverDeliversIsReported()
    {
        string meterName = TestNames.Meter(nameof(ConfiguredTagKeyThatTheWorkloadNeverDeliversIsReported));
        using Meter meter = new Meter(meterName, "1.0.0");
        Counter<long> counter = meter.CreateCounter<long>("requests");

        MetricBudgetOptions options = new MetricBudgetOptions()
            .ForInstrument(meterName, "requests", budget =>
            {
                budget.MaxObservedSeries = 4;
                budget.Tag("route").MaxDistinctValues = 2;
                budget.Tag("tenant").MaxDistinctValues = 1;
            });

        using MetricBudgetSession session = MetricBudgetSession.Start(options);
        counter.Add(1, new KeyValuePair<string, object?>("route", "/a"));

        MetricBudgetReport report = session.Complete();

        Assert.Equal(MetricBudgetOutcome.Passed, report.Outcome);

        MetricBudgetTagResult tenant = report.Rules[0].Instruments[0].Tags.Single(tag => tag.Key == "tenant");
        Assert.True(tenant.IsConfigured);
        Assert.False(tenant.WasObserved);
        Assert.Equal(0, tenant.ObservedDistinctValueCount);
        Assert.True(tenant.IsWithinBudget);
    }

    [Fact]
    public void NullEmptyAndSpecialTagValuesAreDistinguished()
    {
        string meterName = TestNames.Meter(nameof(NullEmptyAndSpecialTagValuesAreDistinguished));
        using Meter meter = new Meter(meterName, "1.0.0");
        Counter<long> counter = meter.CreateCounter<long>("requests");

        MetricBudgetOptions options = new MetricBudgetOptions()
            .ForInstrument(meterName, "requests", budget => budget.MaxObservedSeries = 100);

        using MetricBudgetSession session = MetricBudgetSession.Start(options);

        // A null key, the literal key "null", and an empty key are three different identities.
        counter.Add(1, new KeyValuePair<string, object?>(null!, "value"));
        counter.Add(1, new KeyValuePair<string, object?>("null", "value"));
        counter.Add(1, new KeyValuePair<string, object?>(string.Empty, "value"));

        // A null value and the string value "null" are different identities, and the CLR type is part of identity.
        counter.Add(1, new KeyValuePair<string, object?>("v", null));
        counter.Add(1, new KeyValuePair<string, object?>("v", "null"));
        counter.Add(1, new KeyValuePair<string, object?>("v", 1));
        counter.Add(1, new KeyValuePair<string, object?>("v", "1"));
        counter.Add(1, new KeyValuePair<string, object?>("v", 1.0d));

        MetricBudgetReport report = session.Complete();

        Assert.Equal(MetricBudgetOutcome.Passed, report.Outcome);
        Assert.Equal(8, report.TotalMeasurementsObserved);
        Assert.Equal(8, report.ObservedSeriesCount);

        MetricBudgetInstrumentResult instrument = report.Rules[0].Instruments[0];
        Assert.Contains(instrument.Tags, tag => tag.Key is null);
        Assert.Contains(instrument.Tags, tag => tag.Key == "null");
        Assert.Contains(instrument.Tags, tag => tag.Key == string.Empty);

        MetricBudgetTagResult values = instrument.Tags.Single(tag => tag.Key == "v");
        // null, "null", int 1, string "1", and double 1 all map to different identity descriptors; the double and
        // the int differ because the CLR type is part of identity.
        Assert.Equal(5, values.ObservedDistinctValueCount);
    }

    [Fact]
    public void CompositeAccountingIsPerInstrumentAndPerTag()
    {
        string meterName = TestNames.Meter(nameof(CompositeAccountingIsPerInstrumentAndPerTag));
        using Meter meter = new Meter(meterName, "1.0.0");
        Counter<long> first = meter.CreateCounter<long>("first");
        Counter<long> second = meter.CreateCounter<long>("second");

        MetricBudgetOptions options = new MetricBudgetOptions()
            .ForMeter(meterName, budget =>
            {
                budget.MaxObservedSeries = 4;
                budget.Tag("route").MaxDistinctValues = 2;
            });

        using MetricBudgetSession session = MetricBudgetSession.Start(options);
        first.Add(1, new KeyValuePair<string, object?>("route", "/a"));
        first.Add(1, new KeyValuePair<string, object?>("route", "/b"));
        second.Add(1, new KeyValuePair<string, object?>("route", "/a"));

        MetricBudgetReport report = session.Complete();

        Assert.Equal(MetricBudgetOutcome.Passed, report.Outcome);

        IReadOnlyList<MetricBudgetInstrumentResult> instruments = report.Rules[0].Instruments;
        Assert.Equal(2, instruments.Count);
        Assert.Equal(
            1,
            instruments.Single(instrument => instrument.InstrumentName == "second").ObservedSeriesCount);
        Assert.Equal(
            2,
            instruments.Single(instrument => instrument.InstrumentName == "first").ObservedSeriesCount);

        // Tag budgets are per instrument identity, so the same key is counted separately for each instrument.
        foreach (MetricBudgetInstrumentResult instrument in instruments)
        {
            MetricBudgetTagResult tag = Assert.Single(instrument.Tags);
            Assert.Equal(2, tag.ConfiguredMaxDistinctValues);
            Assert.True(tag.IsWithinBudget);
        }
    }

    [Fact]
    public void OversizedTagValuesKeepIdentityDistinctWithoutRetainingTheValue()
    {
        string meterName = TestNames.Meter(nameof(OversizedTagValuesKeepIdentityDistinctWithoutRetainingTheValue));
        using Meter meter = new Meter(meterName, "1.0.0");
        Counter<long> counter = meter.CreateCounter<long>("requests");

        string sharedPrefix = new string('x', 512);
        string firstValue = sharedPrefix + "-first";
        string secondValue = sharedPrefix + "-second";

        MetricBudgetOptions options = new MetricBudgetOptions
        {
            MaxTagValueLength = 64,
        };
        options.ForInstrument(meterName, "requests", budget =>
        {
            budget.MaxObservedSeries = 2;
            budget.Tag("route").MaxDistinctValues = 2;
        });

        using MetricBudgetSession session = MetricBudgetSession.Start(options);
        counter.Add(1, new KeyValuePair<string, object?>("route", firstValue));
        counter.Add(1, new KeyValuePair<string, object?>("route", secondValue));
        counter.Add(1, new KeyValuePair<string, object?>("route", firstValue));

        MetricBudgetReport report = session.Complete();

        Assert.Equal(MetricBudgetOutcome.Passed, report.Outcome);
        Assert.Equal(3, report.TotalMeasurementsObserved);
        Assert.Equal(2, report.ObservedSeriesCount);
        Assert.Equal(2, report.Rules[0].Instruments[0].Tags[0].ObservedDistinctValueCount);
        Assert.DoesNotContain(sharedPrefix.Substring(0, 32), report.ToDiagnosticString(), StringComparison.Ordinal);
    }
}
