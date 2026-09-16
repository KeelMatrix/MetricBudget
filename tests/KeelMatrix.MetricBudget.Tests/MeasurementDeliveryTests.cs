// Copyright (c) KeelMatrix

using System.Diagnostics.Metrics;

namespace KeelMatrix.MetricBudget.Tests;

/// <summary>
/// Delivery coverage for every numeric measurement type and every supported instrument kind.
/// </summary>
public sealed class MeasurementDeliveryTests
{
    [Fact]
    public void EveryNumericMeasurementTypeIsDelivered()
    {
        string meterName = TestNames.Meter(nameof(EveryNumericMeasurementTypeIsDelivered));
        using Meter meter = new Meter(meterName, "1.0.0");

        Counter<byte> byteCounter = meter.CreateCounter<byte>("m.byte");
        Counter<short> shortCounter = meter.CreateCounter<short>("m.short");
        Counter<int> intCounter = meter.CreateCounter<int>("m.int");
        Counter<long> longCounter = meter.CreateCounter<long>("m.long");
        Counter<float> floatCounter = meter.CreateCounter<float>("m.float");
        Counter<double> doubleCounter = meter.CreateCounter<double>("m.double");
        Counter<decimal> decimalCounter = meter.CreateCounter<decimal>("m.decimal");

        MetricBudgetOptions options = new MetricBudgetOptions()
            .ForMeter(meterName, budget => budget.MaxObservedSeries = 7);

        using MetricBudgetSession session = MetricBudgetSession.Start(options);
        byteCounter.Add(1);
        shortCounter.Add(2);
        intCounter.Add(3);
        longCounter.Add(4);
        floatCounter.Add(5);
        doubleCounter.Add(6);
        decimalCounter.Add(7);

        MetricBudgetReport report = session.Complete();

        Assert.Equal(MetricBudgetOutcome.Passed, report.Outcome);
        Assert.Equal(7, report.ObservedInstrumentCount);
        Assert.Equal(7, report.TotalMeasurementsObserved);
        Assert.Equal(7, report.ObservedSeriesCount);
    }

    [Fact]
    public void EveryInstrumentKindIsDelivered()
    {
        string meterName = TestNames.Meter(nameof(EveryInstrumentKindIsDelivered));
        using Meter meter = new Meter(meterName, "1.0.0");

        Counter<long> counter = meter.CreateCounter<long>("k.counter");
        UpDownCounter<long> upDownCounter = meter.CreateUpDownCounter<long>("k.updown");
        Histogram<long> histogram = meter.CreateHistogram<long>("k.histogram");
        ObservableCounter<long> observableCounter = meter.CreateObservableCounter(
            "k.observable.counter",
            () => new Measurement<long>(1, new KeyValuePair<string, object?>("kind", "observable.counter")));
        ObservableUpDownCounter<long> observableUpDownCounter = meter.CreateObservableUpDownCounter(
            "k.observable.updown",
            () => new Measurement<long>(2, new KeyValuePair<string, object?>("kind", "observable.updown")));
        ObservableGauge<long> observableGauge = meter.CreateObservableGauge(
            "k.observable.gauge",
            () => new Measurement<long>(3, new KeyValuePair<string, object?>("kind", "observable.gauge")));

        MetricBudgetOptions options = new MetricBudgetOptions()
            .ForMeter(meterName, budget => budget.MaxObservedSeries = 6);

        using MetricBudgetSession session = MetricBudgetSession.Start(options);
        counter.Add(1);
        upDownCounter.Add(1);
        histogram.Record(1);
        session.RecordObservableInstruments();

        MetricBudgetReport report = session.Complete();

        _ = observableCounter;
        _ = observableUpDownCounter;
        _ = observableGauge;

        Assert.Equal(MetricBudgetOutcome.Passed, report.Outcome);
        Assert.Equal(6, report.ObservedInstrumentCount);
        Assert.Equal(6, report.TotalMeasurementsObserved);

        IReadOnlyList<MetricBudgetInstrumentResult> instruments = report.Rules[0].Instruments;
        Assert.Contains(instruments, instrument => instrument.InstrumentKind == MetricInstrumentKind.Counter);
        Assert.Contains(instruments, instrument => instrument.InstrumentKind == MetricInstrumentKind.UpDownCounter);
        Assert.Contains(instruments, instrument => instrument.InstrumentKind == MetricInstrumentKind.Histogram);
        Assert.Contains(instruments, instrument => instrument.InstrumentKind == MetricInstrumentKind.ObservableCounter);
        Assert.Contains(instruments, instrument => instrument.InstrumentKind == MetricInstrumentKind.ObservableUpDownCounter);
        Assert.Contains(instruments, instrument => instrument.InstrumentKind == MetricInstrumentKind.ObservableGauge);

        MetricBudgetInstrumentResult observable = instruments.Single(
            instrument => instrument.InstrumentKind == MetricInstrumentKind.ObservableGauge);
        MetricBudgetTagResult tag = Assert.Single(observable.Tags);
        Assert.Equal("kind", tag.Key);
        Assert.Equal(1, tag.ObservedDistinctValueCount);
    }
}
