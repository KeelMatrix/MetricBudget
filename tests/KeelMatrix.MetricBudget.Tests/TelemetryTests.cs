// Copyright (c) KeelMatrix

using System.Diagnostics.Metrics;
using System.Reflection;
using KeelMatrix.MetricBudget.Internal;

namespace KeelMatrix.MetricBudget.Tests;

/// <summary>
/// Telemetry: the field allowlist, identity leakage, and the rule that telemetry never changes a result.
/// </summary>
public sealed class TelemetryTests
{
    [Fact]
    public void AllowlistContainsOnlyCoarseNonIdentityFields()
    {
        string[] expected =
        {
            "package_version",
            "target_framework",
            "os_family",
            "observed_instrument_bucket",
            "configured_rule_count",
            "outcome",
        };

        Assert.Equal(expected, MetricBudgetTelemetrySignal.AllowedFieldNames);

        string[] forbidden =
        {
            "meter_name",
            "instrument_name",
            "tag_name",
            "tag_key",
            "tag_value",
            "metric_value",
            "url",
            "connection_string",
            "application_name",
            "repository",
            "project",
            "file_path",
            "exception_message",
            "stack_trace",
            "budget_text",
        };

        foreach (string candidate in forbidden)
        {
            Assert.False(
                MetricBudgetTelemetrySignal.IsAllowedFieldName(candidate),
                "Field name '" + candidate + "' must not be allowlisted.");
        }
    }

    [Fact]
    public void SignalTypeExposesOnlyCoarseValues()
    {
        PropertyInfo[] properties = typeof(MetricBudgetTelemetrySignal)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        Assert.Equal(6, properties.Length);

        foreach (PropertyInfo property in properties)
        {
            Type type = property.PropertyType;
            bool coarse = type == typeof(string)
                || type == typeof(int)
                || type.IsEnum;
            Assert.True(coarse, "Signal member '" + property.Name + "' must carry a coarse value.");
        }
    }

    [Fact]
    public void SignalCarriesNoMeterInstrumentOrTagIdentity()
    {
        const string meterToken = "HOSTILE-METER-TOKEN";
        const string instrumentToken = "HOSTILE-INSTRUMENT-TOKEN";
        const string tagKeyToken = "HOSTILE-TAGKEY-TOKEN";
        const string tagValueToken = "HOSTILE-TAGVALUE-TOKEN";

        string meterName = TestNames.Meter(nameof(SignalCarriesNoMeterInstrumentOrTagIdentity)) + "." + meterToken;
        string instrumentName = "requests." + instrumentToken;

        using Meter meter = new Meter(meterName, "1.0.0");
        Counter<long> counter = meter.CreateCounter<long>(instrumentName);

        MetricBudgetOptions options = new MetricBudgetOptions()
            .ForInstrument(meterName, instrumentName, budget =>
            {
                budget.MaxObservedSeries = 4;
                budget.Tag("tenant." + tagKeyToken).MaxDistinctValues = 4;
            });

        TestTelemetrySink sink = new TestTelemetrySink();
        MetricBudgetTelemetry.SetSinkForTests(sink);
        try
        {
            using MetricBudgetSession session = MetricBudgetSession.Start(options);
            counter.Add(1, new KeyValuePair<string, object?>("tenant." + tagKeyToken, tagValueToken));
            MetricBudgetReport report = session.Complete();

            Assert.Equal(MetricBudgetOutcome.Passed, report.Outcome);
        }
        finally
        {
            MetricBudgetTelemetry.SetSinkForTests(null);
        }

        MetricBudgetTelemetrySignal signal = Assert.Single(sink.Signals);
        string description = signal.Describe();

        Assert.DoesNotContain(meterToken, description, StringComparison.Ordinal);
        Assert.DoesNotContain(instrumentToken, description, StringComparison.Ordinal);
        Assert.DoesNotContain(tagKeyToken, description, StringComparison.Ordinal);
        Assert.DoesNotContain(tagValueToken, description, StringComparison.Ordinal);
        Assert.DoesNotContain("tests.metricbudget", description, StringComparison.Ordinal);

        foreach (KeyValuePair<string, string> field in signal.ToFields())
        {
            Assert.True(
                MetricBudgetTelemetrySignal.IsAllowedFieldName(field.Key),
                "Signal field '" + field.Key + "' is not allowlisted.");
        }
    }

    [Fact]
    public void SignalIsReportedOncePerObservedCompletionAndNotForEmptySessions()
    {
        string meterName = TestNames.Meter(nameof(SignalIsReportedOncePerObservedCompletionAndNotForEmptySessions));
        using Meter meter = new Meter(meterName, "1.0.0");
        Counter<long> observed = meter.CreateCounter<long>("observed");
        _ = meter.CreateCounter<long>("never.measured");

        TestTelemetrySink sink = new TestTelemetrySink();
        MetricBudgetTelemetry.SetSinkForTests(sink);
        try
        {
            // Constructing and completing a session that matched nothing must not report anything.
            using (MetricBudgetSession empty = MetricBudgetSession.Start(
                new MetricBudgetOptions().ForInstrument(meterName, "no.such.instrument", budget => budget.MaxObservedSeries = 1)))
            {
                Assert.Equal(MetricBudgetOutcome.NoMatchingInstrument, empty.Complete().Outcome);
            }

            // A session that matched an instrument but observed no measurement is not activation either.
            using (MetricBudgetSession unmeasured = MetricBudgetSession.Start(
                new MetricBudgetOptions().ForInstrument(meterName, "never.measured", budget => budget.MaxObservedSeries = 1)))
            {
                Assert.Equal(MetricBudgetOutcome.NoMeasurementsObserved, unmeasured.Complete().Outcome);
            }

            Assert.Empty(sink.Signals);

            using (MetricBudgetSession session = MetricBudgetSession.Start(
                new MetricBudgetOptions().ForInstrument(meterName, "observed", budget => budget.MaxObservedSeries = 1)))
            {
                observed.Add(1);
                MetricBudgetReport first = session.Complete();
                MetricBudgetReport second = session.Complete();

                Assert.Same(first, second);
                Assert.Equal(MetricBudgetOutcome.Passed, first.Outcome);
            }
        }
        finally
        {
            MetricBudgetTelemetry.SetSinkForTests(null);
        }

        MetricBudgetTelemetrySignal signal = Assert.Single(sink.Signals);
        Assert.Contains("outcome=pass", signal.Describe(), StringComparison.Ordinal);
        Assert.Contains("observed_instrument_bucket=1-5", signal.Describe(), StringComparison.Ordinal);
        Assert.Contains("configured_rule_count=1", signal.Describe(), StringComparison.Ordinal);
    }

    [Fact]
    public void FreshSinkRequestsHeartbeatOnTheFirstCompletionAfterActivation()
    {
        string meterName = TestNames.Meter(nameof(FreshSinkRequestsHeartbeatOnTheFirstCompletionAfterActivation));
        using Meter meter = new Meter(meterName, "1.0.0");
        Counter<long> counter = meter.CreateCounter<long>("observed");
        List<string> requests = new List<string>();

        // A fresh sink represents a later process. The shared client owns the already-activated and ISO-week
        // deduplication state; this seam verifies that the sink requests both decisions on its first completion.
        KeelMatrixTelemetrySink sink = new KeelMatrixTelemetrySink(
            () => requests.Add("activation"),
            () => requests.Add("heartbeat"));
        MetricBudgetTelemetry.SetSinkForTests(sink);
        try
        {
            using MetricBudgetSession session = MetricBudgetSession.Start(
                new MetricBudgetOptions().ForInstrument(meterName, "observed", budget => budget.MaxObservedSeries = 1));
            counter.Add(1);
            _ = session.Complete();
        }
        finally
        {
            MetricBudgetTelemetry.SetSinkForTests(null);
        }

        Assert.Equal(2, requests.Count);
        Assert.Equal("activation", requests[0]);
        Assert.Equal("heartbeat", requests[1]);
    }

    [Fact]
    public void TelemetryFailureNeverChangesAVerificationResult()
    {
        string meterName = TestNames.Meter(nameof(TelemetryFailureNeverChangesAVerificationResult));
        using Meter meter = new Meter(meterName, "1.0.0");
        Counter<long> counter = meter.CreateCounter<long>("requests");

        MetricBudgetOptions options = new MetricBudgetOptions()
            .ForInstrument(meterName, "requests", budget => budget.MaxObservedSeries = 1);

        MetricBudgetTelemetry.SetSinkForTests(new ThrowingTelemetrySink());
        MetricBudgetReport report;
        try
        {
            using MetricBudgetSession session = MetricBudgetSession.Start(options);
            counter.Add(1, new KeyValuePair<string, object?>("route", "/a"));
            counter.Add(1, new KeyValuePair<string, object?>("route", "/b"));
            report = session.Complete();
        }
        finally
        {
            MetricBudgetTelemetry.SetSinkForTests(null);
        }

        Assert.Equal(MetricBudgetOutcome.Violation, report.Outcome);
        Assert.Equal(2, report.ObservedSeriesCount);
        Assert.Equal(2, report.TotalMeasurementsObserved);
    }

    [Fact]
    public void DefaultSinkIsSafeWhenTheSharedClientIsUsed()
    {
        string meterName = TestNames.Meter(nameof(DefaultSinkIsSafeWhenTheSharedClientIsUsed));
        using Meter meter = new Meter(meterName, "1.0.0");
        Counter<long> counter = meter.CreateCounter<long>("requests");

        MetricBudgetOptions options = new MetricBudgetOptions()
            .ForInstrument(meterName, "requests", budget => budget.MaxObservedSeries = 1);

        MetricBudgetTelemetry.SetSinkForTests(null);
        using MetricBudgetSession session = MetricBudgetSession.Start(options);
        counter.Add(1);
        MetricBudgetReport report = session.Complete();

        Assert.Equal(MetricBudgetOutcome.Passed, report.Outcome);
    }

    [Fact]
    public void TestHostRunsWithProductionTelemetryOptOut()
    {
        // tests.runsettings sets this for the test host. If it is missing, the suite could pollute production
        // telemetry, so the guard fails loudly instead of silently relying on developer discipline.
        Assert.Equal("1", Environment.GetEnvironmentVariable("KEELMATRIX_NO_TELEMETRY")?.Trim());
    }

    private sealed class TestTelemetrySink : IMetricBudgetTelemetrySink
    {
        private readonly List<MetricBudgetTelemetrySignal> signals = new List<MetricBudgetTelemetrySignal>();

        internal IReadOnlyList<MetricBudgetTelemetrySignal> Signals
        {
            get
            {
                lock (signals)
                {
                    return signals.ToArray();
                }
            }
        }

        public void Report(MetricBudgetTelemetrySignal signal)
        {
            lock (signals)
            {
                signals.Add(signal);
            }
        }
    }

    private sealed class ThrowingTelemetrySink : IMetricBudgetTelemetrySink
    {
        public void Report(MetricBudgetTelemetrySignal signal)
        {
            throw new InvalidOperationException("Simulated telemetry delivery failure for " + signal.PackageVersion);
        }
    }
}
