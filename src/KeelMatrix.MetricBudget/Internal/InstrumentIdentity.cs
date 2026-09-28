// Copyright (c) KeelMatrix

using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Text;

namespace KeelMatrix.MetricBudget.Internal;

/// <summary>
/// Observed instrument identity: meter scope metadata, instrument metadata, and instrument kind.
/// </summary>
/// <remarks>
/// <para>
/// The BCL metrics identity model does not include the <c>Meter.Scope</c> object, and measurement delivery is
/// process-wide per instrument. Meter tags, instrument tags, unit, description, and measurement type are part of
/// this identity because they distinguish otherwise same-name metric streams.
/// </para>
/// <para>
/// Raw static tag values are never retained: the metadata portion is a fixed-size digest of the canonical tag
/// descriptors. The meter scope marker itself is intentionally excluded because it is not measurement identity.
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
        MetadataDigest = default;
        MetadataComplete = true;
        MetadataComponentLengthsValid = true;
        MetadataComponentLength = 0;
    }

    private InstrumentIdentity(
        string meterName,
        string? meterVersion,
        string instrumentName,
        MetricInstrumentKind kind,
        Sha256Digest metadataDigest,
        bool metadataComplete,
        bool metadataComponentLengthsValid,
        int metadataComponentLength)
        : this(meterName, meterVersion, instrumentName, kind)
    {
        MetadataDigest = metadataDigest;
        MetadataComplete = metadataComplete;
        MetadataComponentLengthsValid = metadataComponentLengthsValid;
        MetadataComponentLength = metadataComponentLength;
    }

    internal string MeterName { get; }

    internal string? MeterVersion { get; }

    internal string InstrumentName { get; }

    internal MetricInstrumentKind Kind { get; }

    /// <summary>Fixed-size digest of unit, description, measurement type, meter tags, and instrument tags.</summary>
    internal Sha256Digest MetadataDigest { get; }

    /// <summary>Whether static metadata was fully enumerable and supported by the identity contract.</summary>
    internal bool MetadataComplete { get; }

    /// <summary>Whether every retained static metadata component is within the configured length bound.</summary>
    internal bool MetadataComponentLengthsValid { get; }

    /// <summary>Largest static metadata component length, used for diagnostics-free bounded admission.</summary>
    internal int MetadataComponentLength { get; }

    /// <summary>Stable lowercase token for the instrument kind, used inside series identity text.</summary>
    internal string KindName => KindToken(Kind);

    internal bool HasComponentLengthsAtMost(int maximum)
    {
        return MeterName.Length <= maximum
            && (MeterVersion is null || MeterVersion.Length <= maximum)
            && InstrumentName.Length <= maximum
            && MetadataComponentLengthsValid
            && MetadataComponentLength <= maximum;
    }

    internal static InstrumentIdentity FromInstrument(Instrument instrument)
    {
        // Kept for internal test fixtures that construct the pre-metadata shape directly.
        Meter meter = instrument.Meter;
        return new InstrumentIdentity(
            meter.Name,
            meter.Version,
            instrument.Name,
            DetectKind(instrument));
    }

    internal static InstrumentIdentity FromInstrument(Instrument instrument, FrozenOptions options)
    {
        Meter meter = instrument.Meter;
        string? measurementTypeName = FindMeasurementTypeName(instrument);
        bool metadataComplete = measurementTypeName is not null;
        // The closed measurement type comes from the finite BCL instrument type surface and is always retained as
        // a bounded framework-owned token. MaxInstrumentIdentityLength limits caller-controlled names and text.
        int metadataComponentLength = 0;
        bool componentLengthsValid = true;

        string? unit = instrument.Unit;
        string? description = instrument.Description;
        metadataComponentLength = Math.Max(metadataComponentLength, unit?.Length ?? 0);
        metadataComponentLength = Math.Max(metadataComponentLength, description?.Length ?? 0);
        componentLengthsValid &= (unit?.Length ?? 0) <= options.MaxInstrumentIdentityLength
            && (description?.Length ?? 0) <= options.MaxInstrumentIdentityLength;

        bool meterTagsComplete = TryDigestTags(
            meter.Tags,
            options.MaxInstrumentIdentityLength,
            out Sha256Digest meterTagsDigest);
        bool instrumentTagsComplete = TryDigestTags(
            instrument.Tags,
            options.MaxInstrumentIdentityLength,
            out Sha256Digest instrumentTagsDigest);
        metadataComplete &= meterTagsComplete && instrumentTagsComplete;

        Sha256Digest metadataDigest = default;
        if (metadataComplete && componentLengthsValid)
        {
            StringBuilder canonical = new StringBuilder();
            AppendComponent(canonical, "unit", unit);
            AppendComponent(canonical, "description", description);
            AppendComponent(canonical, "measurement-type", measurementTypeName);
            canonical.Append("meter-tags=").Append(meterTagsDigest.ToHex()).Append(';');
            canonical.Append("instrument-tags=").Append(instrumentTagsDigest.ToHex()).Append(';');
            metadataDigest = Sha256TextHash.Digest(canonical.ToString());
        }

        return new InstrumentIdentity(
            meter.Name,
            meter.Version,
            instrument.Name,
            DetectKind(instrument),
            metadataDigest,
            metadataComplete,
            componentLengthsValid,
            metadataComponentLength);
    }

    /// <summary>
    /// Human-readable identity used in diagnostics. Contains no tag keys, tag values, or static metadata values.
    /// </summary>
    internal string Describe()
    {
        string version = MeterVersion is null
            ? string.Empty
            : " version " + MeterVersion;

        return "meter \"" + MeterName + "\"" + version + ", " + KindToken(Kind) + " \"" + InstrumentName + "\"";
    }

    private static bool TryDigestTags(
        IEnumerable<KeyValuePair<string, object?>>? tags,
        int maximumComponentLength,
        out Sha256Digest digest)
    {
        if (tags is null)
        {
            digest = Sha256TextHash.Digest(string.Empty);
            return true;
        }

        List<KeyValuePair<string, object?>> values = new List<KeyValuePair<string, object?>>();
        try
        {
            using IEnumerator<KeyValuePair<string, object?>> enumerator = tags.GetEnumerator();
            while (enumerator.MoveNext())
            {
                if (values.Count >= maximumComponentLength)
                {
                    digest = default;
                    return false;
                }

                values.Add(enumerator.Current);
            }
        }
        catch
        {
            digest = default;
            return false;
        }

        if (!TagIdentity.TryCreateTagSetKey(
                values.ToArray(),
                maximumComponentLength,
                maximumComponentLength,
                maximumComponentLength,
                out string canonical,
                out _))
        {
            digest = default;
            return false;
        }

        digest = Sha256TextHash.Digest(canonical);
        return true;
    }

    private static void AppendComponent(StringBuilder builder, string name, string? value)
    {
        builder.Append(name).Append('=');
        if (value is null)
        {
            builder.Append("null;");
            return;
        }

        builder.Append(value.Length).Append(':').Append(value).Append(';');
    }

    private static string? FindMeasurementTypeName(Instrument instrument)
    {
        Type? current = instrument.GetType();
        while (current is not null)
        {
            if (current.IsGenericType)
            {
                Type genericDefinition = current.GetGenericTypeDefinition();
                if (genericDefinition == typeof(Instrument<>)
                    || genericDefinition == typeof(ObservableInstrument<>))
                {
                    Type measurementType = current.GetGenericArguments()[0];
                    return measurementType.AssemblyQualifiedName ?? measurementType.FullName;
                }
            }

            current = current.BaseType;
        }

        return null;
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
            && Kind == other.Kind
            && MetadataComplete == other.MetadataComplete
            && MetadataComponentLengthsValid == other.MetadataComponentLengthsValid
            && MetadataDigest.Equals(other.MetadataDigest);
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
            hash = (hash * 397) ^ MetadataDigest.GetHashCode();
            hash = (hash * 397) ^ (MetadataComplete ? 1 : 0);
            hash = (hash * 397) ^ (MetadataComponentLengthsValid ? 1 : 0);
            return hash;
        }
    }

    public override string ToString()
    {
        return Describe();
    }
}
