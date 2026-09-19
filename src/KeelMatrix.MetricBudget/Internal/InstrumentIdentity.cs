// Copyright (c) KeelMatrix

using System.Diagnostics.Metrics;

namespace KeelMatrix.MetricBudget.Internal;

/// <summary>
/// Observed instrument identity: meter name, meter version, instrument name, and instrument kind.
/// </summary>
/// <remarks>
/// <para>
/// The BCL metrics identity model does not include the <c>Meter.Scope</c> object, and measurement delivery is
/// process-wide per instrument, so two <c>Meter</c> instances that share a name and version share one observed
/// identity. That is the same instrumentation-scope identity model used by the OpenTelemetry .NET SDK.
/// </para>
/// <para>
/// The meter version is part of identity, so the same instrument name in two versions of a meter is accounted
/// separately.
/// </para>
/// </remarks>
internal readonly struct InstrumentIdentity : IEquatable<InstrumentIdentity>
{
    internal InstrumentIdentity(
        string meterName,
        string? meterVersion,
        string instrumentName,
        MetricInstrumentKind kind)
    {
        MeterName = meterName;
        MeterVersion = meterVersion;
        InstrumentName = instrumentName;
        Kind = kind;
    }

    internal string MeterName { get; }

    internal string? MeterVersion { get; }

    internal string InstrumentName { get; }

    internal MetricInstrumentKind Kind { get; }

    /// <summary>Stable lowercase token for the instrument kind, used inside series identity text.</summary>
    internal string KindName => KindToken(Kind);

    internal bool HasComponentLengthsAtMost(int maximum)
    {
        return MeterName.Length <= maximum
            && (MeterVersion is null || MeterVersion.Length <= maximum)
            && InstrumentName.Length <= maximum;
    }

    internal static InstrumentIdentity FromInstrument(Instrument instrument)
    {
        Meter meter = instrument.Meter;
        return new InstrumentIdentity(
            meter.Name,
            meter.Version,
            instrument.Name,
            DetectKind(instrument));
    }

    /// <summary>
    /// Human-readable identity used in diagnostics. Contains no tag keys or values.
    /// </summary>
    internal string Describe()
    {
        string version = MeterVersion is null
            ? string.Empty
            : " version " + MeterVersion;

        return "meter \"" + MeterName + "\"" + version + ", " + KindToken(Kind) + " \"" + InstrumentName + "\"";
    }

    private static MetricInstrumentKind DetectKind(Instrument instrument)
    {
        // Instrument kinds are sealed BCL types, and the open generic type name is stable across the supported
        // target frameworks, so type-name comparison avoids reflection on every callback.
        string name = instrument.GetType().Name;

        if (string.Equals(name, CounterName, StringComparison.Ordinal))
        {
            return MetricInstrumentKind.Counter;
        }

        if (string.Equals(name, UpDownCounterName, StringComparison.Ordinal))
        {
            return MetricInstrumentKind.UpDownCounter;
        }

        if (string.Equals(name, HistogramName, StringComparison.Ordinal))
        {
            return MetricInstrumentKind.Histogram;
        }

        if (string.Equals(name, ObservableCounterName, StringComparison.Ordinal))
        {
            return MetricInstrumentKind.ObservableCounter;
        }

        if (string.Equals(name, ObservableUpDownCounterName, StringComparison.Ordinal))
        {
            return MetricInstrumentKind.ObservableUpDownCounter;
        }

        return string.Equals(name, ObservableGaugeName, StringComparison.Ordinal)
            ? MetricInstrumentKind.ObservableGauge
            : MetricInstrumentKind.Unknown;
    }

    private static string KindToken(MetricInstrumentKind kind)
    {
        return kind switch
        {
            MetricInstrumentKind.Counter => "counter",
            MetricInstrumentKind.UpDownCounter => "updowncounter",
            MetricInstrumentKind.Histogram => "histogram",
            MetricInstrumentKind.ObservableCounter => "observablecounter",
            MetricInstrumentKind.ObservableUpDownCounter => "observableupdowncounter",
            MetricInstrumentKind.ObservableGauge => "observablegauge",
            _ => "unknown",
        };
    }

    private static readonly string CounterName = typeof(Counter<int>).Name;
    private static readonly string UpDownCounterName = typeof(UpDownCounter<int>).Name;
    private static readonly string HistogramName = typeof(Histogram<int>).Name;
    private static readonly string ObservableCounterName = typeof(ObservableCounter<int>).Name;
    private static readonly string ObservableUpDownCounterName = typeof(ObservableUpDownCounter<int>).Name;
    private static readonly string ObservableGaugeName = typeof(ObservableGauge<int>).Name;

    public bool Equals(InstrumentIdentity other)
    {
        return string.Equals(MeterName, other.MeterName, StringComparison.Ordinal)
            && string.Equals(MeterVersion, other.MeterVersion, StringComparison.Ordinal)
            && string.Equals(InstrumentName, other.InstrumentName, StringComparison.Ordinal)
            && Kind == other.Kind;
    }

    public override bool Equals(object? obj)
    {
        return obj is InstrumentIdentity other && Equals(other);
    }

    public override int GetHashCode()
    {
        unchecked
        {
            int hash = StringComparer.Ordinal.GetHashCode(MeterName);
            hash = (hash * 397) ^ (MeterVersion is null ? 0 : StringComparer.Ordinal.GetHashCode(MeterVersion));
            hash = (hash * 397) ^ StringComparer.Ordinal.GetHashCode(InstrumentName);
            hash = (hash * 397) ^ (int)Kind;
            return hash;
        }
    }

    public override string ToString()
    {
        return Describe();
    }

}
