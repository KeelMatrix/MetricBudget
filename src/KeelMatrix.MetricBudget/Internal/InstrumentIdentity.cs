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
    internal const int MaxStaticMetadataTextLength = 256;
    internal const int MaxStaticMetadataTagCount = 256;
    internal const int MaxStaticMetadataTagKeyLength = 256;
    internal const int MaxStaticMetadataTagValueLength = 256;

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
        MetadataFailures = StaticMetadataFailure.None;
        IdentityDiscriminator = CreateIdentityDiscriminator();
    }

    private InstrumentIdentity(
        string meterName,
        string? meterVersion,
        string instrumentName,
        MetricInstrumentKind kind,
        Sha256Digest metadataDigest,
        bool metadataComplete,
        StaticMetadataFailure metadataFailures)
    {
        MeterName = meterName;
        MeterVersion = meterVersion;
        InstrumentName = instrumentName;
        Kind = kind;
        MetadataDigest = metadataDigest;
        MetadataComplete = metadataComplete;
        MetadataFailures = metadataFailures;
        IdentityDiscriminator = CreateIdentityDiscriminator();
    }

    internal string MeterName { get; }

    internal string? MeterVersion { get; }

    internal string InstrumentName { get; }

    internal MetricInstrumentKind Kind { get; }

    /// <summary>Fixed-size digest of unit, description, measurement type, meter tags, and instrument tags.</summary>
    internal Sha256Digest MetadataDigest { get; }

    /// <summary>Whether static metadata was fully enumerable and supported by the identity contract.</summary>
    internal bool MetadataComplete { get; }

    /// <summary>Static metadata admission failures, when the identity could not be fully represented.</summary>
    internal StaticMetadataFailure MetadataFailures { get; }

    /// <summary>Privacy-safe discriminator for the complete instrument identity.</summary>
    internal string IdentityDiscriminator { get; }

    /// <summary>Stable lowercase token for the instrument kind, used inside series identity text.</summary>
    internal string KindName => KindToken(Kind);

    internal bool HasNameComponentsAtMost(int maximum)
    {
        return HasNameComponentsAtMost(MeterName, MeterVersion, InstrumentName, maximum);
    }

    internal static bool HasNameComponentsAtMost(
        string meterName,
        string? meterVersion,
        string instrumentName,
        int maximum)
    {
        return meterName.Length <= maximum
            && (meterVersion is null || meterVersion.Length <= maximum)
            && instrumentName.Length <= maximum;
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
        StaticMetadataFailure metadataFailures = StaticMetadataFailure.None;
        if (measurementTypeName is null || measurementTypeName.Length > MaxStaticMetadataTextLength)
        {
            metadataFailures |= StaticMetadataFailure.MeasurementType;
        }

        // MaxInstrumentIdentityLength applies to names. Static metadata has separate caller options and fixed hard
        // ceilings so a delivery-tag limit can never silently reject a published instrument.
        string? unit = instrument.Unit;
        string? description = instrument.Description;
        int maximumTextLength = Math.Min(
            options.MaxStaticMetadataTextLength,
            MaxStaticMetadataTextLength);
        bool componentLengthsValid = (unit?.Length ?? 0) <= maximumTextLength
            && (description?.Length ?? 0) <= maximumTextLength;
        if (!componentLengthsValid)
        {
            metadataFailures |= StaticMetadataFailure.TextLength;
        }

        metadataFailures |= TryDigestTags(
            meter.Tags,
            Math.Min(options.MaxStaticMetadataTagCount, MaxStaticMetadataTagCount),
            Math.Min(options.MaxStaticMetadataTagKeyLength, MaxStaticMetadataTagKeyLength),
            Math.Min(options.MaxStaticMetadataTagValueLength, MaxStaticMetadataTagValueLength),
            StaticMetadataFailure.MeterTagCount,
            StaticMetadataFailure.MeterTagKeyLength,
            StaticMetadataFailure.MeterTagValueLength,
            StaticMetadataFailure.MeterTagValue,
            StaticMetadataFailure.MeterTagEnumeration,
            out Sha256Digest meterTagsDigest);
        metadataFailures |= TryDigestTags(
            instrument.Tags,
            Math.Min(options.MaxStaticMetadataTagCount, MaxStaticMetadataTagCount),
            Math.Min(options.MaxStaticMetadataTagKeyLength, MaxStaticMetadataTagKeyLength),
            Math.Min(options.MaxStaticMetadataTagValueLength, MaxStaticMetadataTagValueLength),
            StaticMetadataFailure.InstrumentTagCount,
            StaticMetadataFailure.InstrumentTagKeyLength,
            StaticMetadataFailure.InstrumentTagValueLength,
            StaticMetadataFailure.InstrumentTagValue,
            StaticMetadataFailure.InstrumentTagEnumeration,
            out Sha256Digest instrumentTagsDigest);
        bool metadataComplete = metadataFailures == StaticMetadataFailure.None;

        Sha256Digest metadataDigest = default;
        if (metadataComplete)
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
            metadataFailures);
    }

    /// <summary>
    /// Human-readable identity used in diagnostics. Contains no tag keys, tag values, or static metadata values.
    /// </summary>
    internal string Describe()
    {
        string version = MeterVersion is null
            ? string.Empty
            : " version " + MeterVersion;

        return "meter \"" + MeterName + "\"" + version + ", " + KindToken(Kind) + " \"" + InstrumentName
            + "\" (identity discriminator " + IdentityDiscriminator + ")";
    }

    private static StaticMetadataFailure TryDigestTags(
        IEnumerable<KeyValuePair<string, object?>>? tags,
        int maximumTagCount,
        int maximumTagKeyLength,
        int maximumTagValueLength,
        StaticMetadataFailure tagCountFailure,
        StaticMetadataFailure tagKeyLengthFailure,
        StaticMetadataFailure tagValueLengthFailure,
        StaticMetadataFailure tagValueFailure,
        StaticMetadataFailure enumerationFailure,
        out Sha256Digest digest)
    {
        if (tags is null)
        {
            digest = Sha256TextHash.Digest(string.Empty);
            return StaticMetadataFailure.None;
        }

        List<KeyValuePair<string, object?>> values = new List<KeyValuePair<string, object?>>();
        StaticMetadataFailure failure = StaticMetadataFailure.None;
        try
        {
            using IEnumerator<KeyValuePair<string, object?>> enumerator = tags.GetEnumerator();
            while (enumerator.MoveNext())
            {
                if (values.Count >= maximumTagCount)
                {
                    failure |= tagCountFailure;
                    break;
                }

                KeyValuePair<string, object?> value = enumerator.Current;
                values.Add(value);
                if (value.Key is not null && value.Key.Length > maximumTagKeyLength)
                {
                    failure |= tagKeyLengthFailure;
                }

                if (!TagIdentity.IsSupportedValueWithinLength(value.Value, maximumTagValueLength))
                {
                    failure |= value.Value is string text && text.Length > maximumTagValueLength
                        ? tagValueLengthFailure
                        : tagValueFailure;
                }
            }
        }
        catch
        {
            digest = default;
            return failure | enumerationFailure;
        }

        if (failure != StaticMetadataFailure.None)
        {
            digest = default;
            return failure;
        }

        if (!TagIdentity.TryCreateTagSetKey(
                values.ToArray(),
                maximumTagValueLength,
                maximumTagCount,
                maximumTagKeyLength,
                out string canonical,
                out _))
        {
            digest = default;
            return enumerationFailure;
        }

        digest = Sha256TextHash.Digest(canonical);
        return StaticMetadataFailure.None;
    }

    private string CreateIdentityDiscriminator()
    {
        StringBuilder canonical = new StringBuilder();
        AppendComponent(canonical, "meter", MeterName);
        AppendComponent(canonical, "version", MeterVersion);
        AppendComponent(canonical, "instrument", InstrumentName);
        AppendComponent(canonical, "kind", KindName);
        AppendComponent(canonical, "metadata", MetadataDigest.ToHex());
        return Sha256TextHash.Digest(canonical.ToString()).ToHex();
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
                    string typeName = measurementType.FullName ?? measurementType.Name;
                    string? assemblyName = measurementType.Assembly.GetName().Name;
                    // Core framework types use different implementation assembly names on net8.0 and .NET
                    // Framework. Their full names are the stable contract; retain the simple assembly name for
                    // application types so two types with the same full name cannot merge accidentally.
                    return assemblyName is "System.Private.CoreLib" or "mscorlib"
                        ? typeName
                        : (assemblyName is null ? typeName : assemblyName + ":" + typeName);
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
            && MetadataFailures == other.MetadataFailures
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
            hash = (hash * 397) ^ (int)MetadataFailures;
            return hash;
        }
    }

    public override string ToString()
    {
        return Describe();
    }
}

[Flags]
internal enum StaticMetadataFailure
{
    None = 0,
    TextLength = 1 << 0,
    MeasurementType = 1 << 1,
    MeterTagCount = 1 << 2,
    MeterTagKeyLength = 1 << 3,
    MeterTagValueLength = 1 << 4,
    MeterTagValue = 1 << 5,
    MeterTagEnumeration = 1 << 6,
    InstrumentTagCount = 1 << 7,
    InstrumentTagKeyLength = 1 << 8,
    InstrumentTagValueLength = 1 << 9,
    InstrumentTagValue = 1 << 10,
    InstrumentTagEnumeration = 1 << 11,
}
