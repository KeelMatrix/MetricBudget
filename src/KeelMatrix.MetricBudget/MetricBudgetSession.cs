// Copyright (c) KeelMatrix

using System.Diagnostics.Metrics;
using KeelMatrix.MetricBudget.Internal;

namespace KeelMatrix.MetricBudget;

/// <summary>
/// Observes the metrics a workload emits and accounts observed cardinality against a configured budget.
/// </summary>
/// <remarks>
/// <para>
/// Start the session before the workload runs, execute the workload, then call <see cref="Complete"/> and assert
/// the report. The session is a <c>MeterListener</c>, so it observes the measurements the process actually emits;
/// it never inspects metric values, and it needs no collector, exporter, or backend.
/// </para>
/// <para>
/// <b>Containment.</b> <c>MeterListener.Dispose</c> does not stop measurement delivery for instruments that the
/// listener already enabled: callbacks can keep firing after disposal, and <c>Instrument.Enabled</c> describes all
/// listeners rather than this session alone. This session therefore disables measurement events explicitly for
/// every instrument it enabled, both when <see cref="Complete"/> runs and when it is disposed without completing.
/// Disposal is not the containment mechanism; the explicit disable is. The flag may remain true when another
/// listener still has the instrument enabled.
/// </para>
/// <para>
/// <b>Isolation.</b> Instrument publication is process-global, so a session sees instruments created by any code
/// in the process, including other tests running in parallel. Delivery is scoped: a session receives measurements
/// only from the instruments it enabled. A session is started, completed, and disposed within one workload, and a
/// completed session stops delivery for everything it enabled.
/// </para>
/// <para>
/// <b>Bounded memory.</b> A session keeps fixed-size digests under explicit safety bounds. Filling a bound does not
/// itself make tracking incomplete; when an additional observation or state entry is rejected, the report says
/// tracking was incomplete. A proven budget breach or selector conflict has higher outcome precedence, so consumers
/// must inspect the completeness flags as well as <see cref="MetricBudgetReport.Outcome"/>.
/// </para>
/// </remarks>
public sealed class MetricBudgetSession : IDisposable
{
    private readonly MetricBudgetState state;
    private readonly MeterListener listener;
    private readonly object completionLock = new();

    private MetricBudgetReport? report;
    private bool disposed;

    private MetricBudgetSession(FrozenOptions options)
    {
        state = new MetricBudgetState(options);
        listener = new MeterListener
        {
            InstrumentPublished = state.OnInstrumentPublished,
        };

        // A listener only receives measurements for the numeric types it registered, so all seven supported
        // measurement types are registered. The BCL rejects every other measurement type when the instrument is
        // created, which is why this set is complete rather than a sample.
        listener.SetMeasurementEventCallback<byte>(state.OnMeasurement);
        listener.SetMeasurementEventCallback<short>(state.OnMeasurement);
        listener.SetMeasurementEventCallback<int>(state.OnMeasurement);
        listener.SetMeasurementEventCallback<long>(state.OnMeasurement);
        listener.SetMeasurementEventCallback<float>(state.OnMeasurement);
        listener.SetMeasurementEventCallback<double>(state.OnMeasurement);
        listener.SetMeasurementEventCallback<decimal>(state.OnMeasurement);
    }

    /// <summary>
    /// Starts observing for the configured selection.
    /// </summary>
    /// <param name="options">Budget and safety configuration for this session.</param>
    /// <returns>A started session.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is null.</exception>
    /// <exception cref="MetricBudgetConfigurationException">
    /// The options cannot describe a verification, for example because no rule is configured or a rule declares no
    /// limit. Invalid configuration fails before any measurement is observed.
    /// </exception>
    public static MetricBudgetSession Start(MetricBudgetOptions options)
    {
        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        FrozenOptions frozen = options.Freeze();
        MetricBudgetSession session = new MetricBudgetSession(frozen);
        session.listener.Start();
        return session;
    }

    /// <summary>
    /// Whether this session has already stopped observing and produced a report.
    /// </summary>
    public bool IsCompleted
    {
        get
        {
            lock (completionLock)
            {
                return report is not null;
            }
        }
    }

    /// <summary>
    /// Runs the observable instrument callbacks for the instruments this session selected.
    /// </summary>
    /// <remarks>
    /// Observable instruments deliver measurements only when their callbacks run, and the BCL never runs them at
    /// listener start. Call this method at the intended observation points even when OpenTelemetry or another
    /// metrics SDK is collecting: observable callbacks deliver to the specific listener that requested collection,
    /// so another listener's collection does not populate this session. Calling it again records another measurement
    /// per observable instrument, exactly as the BCL does, and calling it after the session completed does nothing
    /// because every selected instrument has been disabled for this session.
    /// </remarks>
    public void RecordObservableInstruments()
    {
        listener.RecordObservableInstruments();
    }

    /// <summary>
    /// Stops observing, disables every instrument this session enabled, and returns the report.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The returned report describes the workload executed before this call. Measurements that the workload
    /// delivers afterwards are not part of the verification, so finish the workload first.
    /// </para>
    /// <para>
    /// The method is idempotent: later calls return the same report.
    /// </para>
    /// </remarks>
    /// <returns>The verification report.</returns>
    /// <exception cref="ObjectDisposedException">
    /// The session was disposed without completing, which ends its lifetime without producing a report.
    /// </exception>
    public MetricBudgetReport Complete()
    {
        MetricBudgetReport completed;
        lock (completionLock)
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(MetricBudgetSession));
            }

            if (report is null)
            {
                report = Stop();
                completed = report;
            }
            else
            {
                return report;
            }
        }

        if (completed.ObservedInstrumentCount > 0)
        {
            // Activation means a completed verification that observed at least one selected instrument.
            MetricBudgetTelemetry.ReportCompleted(MetricBudgetTelemetrySignal.Create(completed));
        }

        return completed;
    }

    /// <summary>
    /// Stops observing and releases the listener.
    /// </summary>
    /// <remarks>
    /// Disposal stops the session and disables every instrument it enabled, but it does not produce a report. Call
    /// <see cref="Complete"/> before disposing to keep the verification result.
    /// </remarks>
    public void Dispose()
    {
        lock (completionLock)
        {
            if (disposed)
            {
                return;
            }

            if (report is null)
            {
                _ = Stop();
            }

            disposed = true;
        }
    }

    private MetricBudgetReport Stop()
    {
        Instrument[] instruments = state.BeginStop();

        // Explicit disable for every instrument this session enabled. Disposing the listener alone does not stop
        // delivery, so this is the containment mechanism rather than disposal.
        for (int i = 0; i < instruments.Length; i++)
        {
            try
            {
                listener.DisableMeasurementEvents(instruments[i]);
            }
            catch (ObjectDisposedException)
            {
                break;
            }
        }

        listener.Dispose();

        SessionSnapshot snapshot = state.CreateSnapshot();
        return ReportBuilder.Build(state.Options, snapshot);
    }
}
