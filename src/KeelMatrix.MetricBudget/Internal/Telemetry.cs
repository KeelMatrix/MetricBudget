// Copyright (c) KeelMatrix

using KeelMatrix.Telemetry;

namespace KeelMatrix.MetricBudget.Internal;

internal interface IMetricBudgetTelemetryClient
{
    void TrackActivation();

    void TrackHeartbeat();
}

/// <summary>
/// Requests the shared activation and heartbeat signals for an eligible completed verification.
/// </summary>
/// <remarks>
/// Product eligibility is checked by <see cref="MetricBudgetSession.Complete"/>. The shared telemetry client owns
/// opt-out handling, event deduplication and cadence, identity, delivery, and failure handling.
/// </remarks>
internal static class MetricBudgetTelemetry
{
    internal const string ToolName = "MetricBudget";

    private static readonly IMetricBudgetTelemetryClient Shared = new SharedTelemetryClient();
    private static IMetricBudgetTelemetryClient? testOverride;

    /// <summary>Replaces the client so tests can inspect signal requests without touching the shared client.</summary>
    internal static void SetClientForTests(IMetricBudgetTelemetryClient? client)
    {
        Volatile.Write(ref testOverride, client);
    }

    internal static void ReportCompleted()
    {
        IMetricBudgetTelemetryClient client = Volatile.Read(ref testOverride) ?? Shared;
        client.TrackActivation();
        client.TrackHeartbeat();
    }

    private sealed class SharedTelemetryClient : IMetricBudgetTelemetryClient
    {
        private readonly Client client = new(ToolName, typeof(MetricBudgetSession));

        public void TrackActivation() => client.TrackActivation();

        public void TrackHeartbeat() => client.TrackHeartbeat();
    }
}
