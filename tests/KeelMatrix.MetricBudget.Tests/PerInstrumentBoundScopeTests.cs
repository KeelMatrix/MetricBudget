// Copyright (c) KeelMatrix

using System.Diagnostics.Metrics;

namespace KeelMatrix.MetricBudget.Tests;

/// <summary>
/// Pins the documented scope of the series safety bound: it is enforced per instrument identity, so one session
/// can retain more series than a single <see cref="MetricBudgetOptions.MaxTrackedSeries"/> value.
/// </summary>
public sealed class PerInstrumentBoundScopeTests
{
    [Fact]
    public void SeriesSafetyBoundAppliesPerInstrumentIdentityAndNotAcrossTheSession()
    {
        string meterName = TestNames.Meter(nameof(SeriesSafetyBoundAppliesPerInstrumentIdentityAndNotAcrossTheSession));
        using Meter meter = new Meter(meterName, "1.0.0");
        Counter<long> requests = meter.CreateCounter<long>("requests");
        Histogram<long> durations = meter.CreateHistogram<long>("durations");

        const int seriesBound = 64;
        const int generatedSeriesPerInstrument = 100;
        const int instrumentCount = 2;

        MetricBudgetOptions options = new MetricBudgetOptions
        {
            MaxTrackedSeries = seriesBound,
        };
        options.ForMeter(meterName, budget => budget.MaxObservedSeries = 10_000);

        using MetricBudgetSession session = MetricBudgetSession.Start(options);
        for (int i = 0; i < generatedSeriesPerInstrument; i++)
        {
            requests.Add(1, new KeyValuePair<string, object?>("tenant", i));
            durations.Record(i, new KeyValuePair<string, object?>("tenant", i));
        }

        MetricBudgetReport report = session.Complete();

        // Each instrument keeps its own bound, so the session total is instrumentCount times the bound.
        Assert.Equal(instrumentCount, report.ObservedInstrumentCount);
        Assert.Equal(seriesBound, report.Safety.MaxTrackedSeries);
        Assert.True(report.Safety.SeriesTrackingIncomplete);
        Assert.Equal(MetricBudgetOutcome.ObservationIncomplete, report.Outcome);

        MetricBudgetInstrumentResult[] instruments = report.Rules[0].Instruments.ToArray();
        Assert.Equal(instrumentCount, instruments.Length);

        foreach (MetricBudgetInstrumentResult instrument in instruments)
        {
            Assert.Equal(seriesBound, instrument.ObservedSeriesCount);
            Assert.Equal(generatedSeriesPerInstrument - seriesBound, instrument.UntrackedSeriesObservations);
            Assert.True(instrument.SeriesTrackingIncomplete);
        }

        int sessionObservedSeries = instruments.Sum(instrument => instrument.ObservedSeriesCount);
        Assert.Equal(seriesBound * instrumentCount, sessionObservedSeries);

        // The measurement this guards: one session holds more series than a single MaxTrackedSeries value, which
        // is exactly why the documentation must not describe the bound as one session-wide pool.
        Assert.True(sessionObservedSeries > seriesBound);
        Assert.Equal(generatedSeriesPerInstrument * instrumentCount, report.TotalMeasurementsObserved);
        Assert.True(report.AccountingIsConsistent);
    }
}
