// Copyright (c) KeelMatrix

using System.Diagnostics.Metrics;
using System.Reflection;
using KeelMatrix.MetricBudget.Internal;

namespace KeelMatrix.MetricBudget.Tests;

/// <summary>Telemetry calls: shared signals, the no-payload boundary, and product eligibility.</summary>
public sealed class TelemetryTests
{
    private static readonly string[] ExpectedSignalMethods = ["TrackActivation", "TrackHeartbeat"];

    [Fact]
    public void TelemetryClientSeamAcceptsNoProductPayload()
    {
        string[] methodNames = typeof(IMetricBudgetTelemetryClient)
            .GetMethods()
            .OrderBy(static method => method.Name, StringComparer.Ordinal)
            .Select(static method => method.Name)
            .ToArray();

        Assert.Equal(ExpectedSignalMethods, methodNames);
        Assert.All(
            typeof(IMetricBudgetTelemetryClient).GetMethods(),
            static method => Assert.Empty(method.GetParameters()));
    }

    [Fact]
    public void CompletedObservedVerificationsRequestBothSignalsAndEmptyVerificationsDoNot()
    {
        string meterName = TestNames.Meter(nameof(CompletedObservedVerificationsRequestBothSignalsAndEmptyVerificationsDoNot));
        using Meter meter = new(meterName, "1.0.0");
        Counter<long> observed = meter.CreateCounter<long>("observed");
        _ = meter.CreateCounter<long>("never.measured");

        RecordingTelemetryClient client = new();
        MetricBudgetTelemetry.SetClientForTests(client);
        try
        {
            using (MetricBudgetSession empty = MetricBudgetSession.Start(
                new MetricBudgetOptions().ForInstrument(meterName, "no.such.instrument", budget => budget.MaxObservedSeries = 1)))
            {
                Assert.Equal(MetricBudgetOutcome.NoMatchingInstrument, empty.Complete().Outcome);
            }

            using (MetricBudgetSession unmeasured = MetricBudgetSession.Start(
                new MetricBudgetOptions().ForInstrument(meterName, "never.measured", budget => budget.MaxObservedSeries = 1)))
            {
                Assert.Equal(MetricBudgetOutcome.NoMeasurementsObserved, unmeasured.Complete().Outcome);
            }

            Assert.Empty(client.SignalRequests);

            using (MetricBudgetSession passing = MetricBudgetSession.Start(
                new MetricBudgetOptions().ForInstrument(meterName, "observed", budget => budget.MaxObservedSeries = 1)))
            {
                observed.Add(1);
                MetricBudgetReport first = passing.Complete();
                MetricBudgetReport repeated = passing.Complete();

                Assert.Same(first, repeated);
                Assert.Equal(MetricBudgetOutcome.Passed, first.Outcome);
            }

            using (MetricBudgetSession violating = MetricBudgetSession.Start(
                new MetricBudgetOptions().ForInstrument(meterName, "observed", budget => budget.MaxObservedSeries = 1)))
            {
                observed.Add(1, new KeyValuePair<string, object?>("route", "/first"));
                observed.Add(1, new KeyValuePair<string, object?>("route", "/second"));
                Assert.Equal(MetricBudgetOutcome.Violation, violating.Complete().Outcome);
            }
        }
        finally
        {
            MetricBudgetTelemetry.SetClientForTests(null);
        }

        string[] expected = [
            "TrackActivation",
            "TrackHeartbeat",
            "TrackActivation",
            "TrackHeartbeat",
        ];
        Assert.Equal(expected, client.SignalRequests);
    }

    [Fact]
    public void DefaultClientIsSafeWhenTheSharedPackageIsUsed()
    {
        string meterName = TestNames.Meter(nameof(DefaultClientIsSafeWhenTheSharedPackageIsUsed));
        using Meter meter = new(meterName, "1.0.0");
        Counter<long> counter = meter.CreateCounter<long>("requests");

        MetricBudgetTelemetry.SetClientForTests(null);
        using MetricBudgetSession session = MetricBudgetSession.Start(
            new MetricBudgetOptions().ForInstrument(meterName, "requests", budget => budget.MaxObservedSeries = 1));
        counter.Add(1);
        MetricBudgetReport report = session.Complete();

        Assert.Equal(MetricBudgetOutcome.Passed, report.Outcome);
    }

    [Fact]
    public void TestHostRunsWithProductionTelemetryOptOut()
    {
        // tests.runsettings sets this for the test host so repository activity cannot enter demand data.
        Assert.Equal("1", Environment.GetEnvironmentVariable("KEELMATRIX_NO_TELEMETRY")?.Trim());
    }

    private sealed class RecordingTelemetryClient : IMetricBudgetTelemetryClient
    {
        private readonly List<string> signalRequests = [];

        internal IReadOnlyList<string> SignalRequests => signalRequests;

        public void TrackActivation() => signalRequests.Add(nameof(TrackActivation));

        public void TrackHeartbeat() => signalRequests.Add(nameof(TrackHeartbeat));
    }
}
