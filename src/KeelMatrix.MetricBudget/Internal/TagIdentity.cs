// Copyright (c) KeelMatrix

using System.Globalization;

namespace KeelMatrix.MetricBudget.Internal;

/// <summary>
/// One delivered tag, reduced to the two identity fields the accountant needs.
/// </summary>
internal readonly struct TagField
{
    internal TagField(string keyField, Sha256Digest valueDigest)
    {
        KeyField = keyField;
        ValueDigest = valueDigest;
    }

    /// <summary>Encoded delivered key. A null delivered key uses the bare marker <c>null</c>.</summary>
    internal string KeyField { get; }

    /// <summary>Digest of the value descriptor, used for bounded per-tag distinct-value accounting.</summary>
    internal Sha256Digest ValueDigest { get; }
}

/// <summary>
/// Deterministic, order-independent identity for a delivered tag set.
/// </summary>
/// <remarks>
/// <para>The identity rule is normative and is documented for users in <c>docs/series-identity.md</c>:</para>
/// <list type="bullet">
/// <item>Every delivered tag becomes one entry: a key field, U+001E, and a value field.</item>
/// <item>A key field is <c>{length}:{key}</c>, or the bare marker <c>null</c> when the delivered key is
/// <see langword="null"/>. Length-prefixed fields always start with an ASCII digit, so no delivered key can
/// produce the bare marker.</item>
/// <item>A value field is <c>{descriptorLength}:{descriptor}</c>.</item>
/// <item>A descriptor is <c>{CLR type full name}:{invariant text}</c>, so <c>int 1</c> and <c>string "1"</c> are
/// different identities, and a <see langword="null"/> value is the descriptor <c>null</c>.</item>
/// <item>A value whose invariant text is longer than the configured bound becomes
/// <c>{type}#chars={count}#sha256={hex}</c>, so one pathological value cannot inflate an identity.</item>
/// <item>Entries are sorted with <see cref="StringComparer.Ordinal"/> and joined with U+001F, so tag order does
/// not change identity.</item>
/// <item>Duplicate keys are retained, so a tag set is a sorted multiset rather than a set.</item>
/// </list>
/// <para>
/// The accountant retains only fixed-size digests of these identities, never the canonical text itself.
/// </para>
/// </remarks>
internal static class TagIdentity
{
    /// <summary>Bare key-field marker for a delivered <see langword="null"/> tag key.</summary>
    internal const string NullKeyMarker = "null";

    internal const char EntrySeparator = '\u001F';
    internal const char FieldSeparator = '\u001E';

    private static readonly TagField[] NoTagFields = Array.Empty<TagField>();

    /// <summary>
    /// Builds the canonical tag-set identity and the per-tag accounting fields for one delivered measurement.
    /// </summary>
    /// <param name="tags">Tag span delivered by <c>MeterListener</c>. Only valid during the callback.</param>
    /// <param name="maxValueLength">Bound applied to a tag value's invariant text.</param>
    /// <param name="fields">Per-tag accounting fields in delivered order.</param>
    /// <returns>The canonical tag-set identity text used to derive the observed-series identity.</returns>
    internal static string CreateTagSetKey(
        ReadOnlySpan<KeyValuePair<string, object?>> tags,
        int maxValueLength,
        out TagField[] fields)
    {
        if (tags.Length == 0)
        {
            fields = NoTagFields;
            return string.Empty;
        }

        fields = new TagField[tags.Length];
        string[] entries = new string[tags.Length];

        for (int i = 0; i < tags.Length; i++)
        {
            string? key = tags[i].Key;
            string descriptor = DescribeValue(tags[i].Value, maxValueLength);

            fields[i] = new TagField(EncodeKeyField(key), Sha256TextHash.Digest(descriptor));
            entries[i] = EncodeEntry(key, descriptor);
        }

        Array.Sort(entries, StringComparer.Ordinal);
        return string.Join(EntrySeparator.ToString(), entries);
    }

    /// <summary>
    /// Key field for one delivered tag key. This is also the grouping key for per-tag distinct-value accounting,
    /// so a <see langword="null"/> key and the literal key <c>"null"</c> are counted separately.
    /// </summary>
    internal static string EncodeKeyField(string? key)
    {
        return key is null ? NullKeyMarker : EncodeField(key);
    }

    /// <summary>
    /// Reverses <see cref="EncodeKeyField"/> for diagnostics. Returns <see langword="null"/> when the delivered
    /// key was <see langword="null"/>.
    /// </summary>
    internal static string? DecodeKeyField(string keyField)
    {
        if (string.Equals(keyField, NullKeyMarker, StringComparison.Ordinal))
        {
            return null;
        }

        int separator = keyField.IndexOf(':');
        return separator < 0 ? keyField : keyField.Substring(separator + 1);
    }

    internal static string DescribeValue(object? value, int maxValueLength)
    {
        if (value is null)
        {
            return "null";
        }

        Type type = value.GetType();
        string typeName = type.FullName ?? type.Name;

        string text = value is string textValue
            ? textValue
            : value is IFormattable formattable
                ? formattable.ToString(null, CultureInfo.InvariantCulture) ?? string.Empty
                : value.ToString() ?? string.Empty;

        if (maxValueLength > 0 && text.Length > maxValueLength)
        {
            return typeName
                + "#chars=" + text.Length.ToString(CultureInfo.InvariantCulture)
                + "#sha256=" + Sha256TextHash.HexDigest(text);
        }

        return typeName + ":" + text;
    }

    private static string EncodeEntry(string? key, string descriptor)
    {
        return EncodeKeyField(key) + FieldSeparator + EncodeField(descriptor);
    }

    private static string EncodeField(string field)
    {
        return field.Length.ToString(CultureInfo.InvariantCulture) + ":" + field;
    }

    /// <summary>
    /// Canonical observed-series identity for one instrument identity and one canonical tag set.
    /// </summary>
    /// <remarks>
    /// Every field is length-prefixed or the bare <c>null</c> marker, so two different instrument identities can
    /// never produce the same series identity.
    /// </remarks>
    internal static string CreateSeriesKey(InstrumentIdentity instrument, string tagSetKey)
    {
        return EncodeField(instrument.MeterName)
            + EntrySeparator
            + (instrument.MeterVersion is null ? NullKeyMarker : EncodeField(instrument.MeterVersion))
            + EntrySeparator
            + EncodeField(instrument.InstrumentName)
            + EntrySeparator
            + EncodeField(instrument.KindName)
            + EntrySeparator
            + tagSetKey;
    }
}
