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

internal enum TagIdentityFailure
{
    TooManyTags,
    TagKeyTooLong,
    UnsupportedValue,
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
/// <item>Only the documented primitive values, strings, GUIDs, date/time values, and time spans are supported.
/// Their exact type and lossless representation define identity; unsupported objects are rejected as incomplete and
/// are never formatted.</item>
/// <item>Strings are sequences of UTF-16 code units, including unpaired surrogates. Oversized strings become
/// <c>{type}#chars={count}#sha256={hex}</c>, where the digest is over those exact code units.</item>
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
    /// <param name="maxTagCount">Maximum number of delivered tags admitted to identity construction.</param>
    /// <param name="maxTagKeyLength">Maximum length of a delivered tag key admitted to identity construction.</param>
    /// <param name="tagSetKey">Canonical tag-set identity text, when construction succeeds.</param>
    /// <param name="fields">Per-tag accounting fields in delivered order.</param>
    /// <returns>The canonical tag-set identity text used to derive the observed-series identity.</returns>
    internal static bool TryCreateTagSetKey(
        ReadOnlySpan<KeyValuePair<string, object?>> tags,
        int maxValueLength,
        int maxTagCount,
        int maxTagKeyLength,
        out string tagSetKey,
        out TagField[] fields)
    {
        if (tags.Length > maxTagCount)
        {
            fields = NoTagFields;
            tagSetKey = string.Empty;
            LastFailure = TagIdentityFailure.TooManyTags;
            return false;
        }

        if (tags.Length == 0)
        {
            fields = NoTagFields;
            tagSetKey = string.Empty;
            LastFailure = null;
            return true;
        }

        fields = new TagField[tags.Length];
        string[] entries = new string[tags.Length];

        for (int i = 0; i < tags.Length; i++)
        {
            string? key = tags[i].Key;
            if (key is not null && key.Length > maxTagKeyLength)
            {
                fields = NoTagFields;
                tagSetKey = string.Empty;
                LastFailure = TagIdentityFailure.TagKeyTooLong;
                return false;
            }

            if (!TryDescribeValue(tags[i].Value, maxValueLength, out string descriptor))
            {
                fields = NoTagFields;
                tagSetKey = string.Empty;
                LastFailure = TagIdentityFailure.UnsupportedValue;
                return false;
            }

            fields[i] = new TagField(EncodeKeyField(key), Sha256TextHash.Digest(descriptor));
            entries[i] = EncodeEntry(key, descriptor);
        }

        Array.Sort(entries, StringComparer.Ordinal);
        tagSetKey = string.Join(EntrySeparator.ToString(), entries);
        LastFailure = null;
        return true;
    }

    [ThreadStatic]
    private static TagIdentityFailure? LastFailure;

    internal static TagIdentityFailure GetLastFailure()
    {
        TagIdentityFailure failure = LastFailure ?? TagIdentityFailure.UnsupportedValue;
        LastFailure = null;
        return failure;
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
        return TryDescribeValue(value, maxValueLength, out string descriptor)
            ? descriptor
            : "unsupported";
    }

    private static bool TryDescribeValue(object? value, int maxValueLength, out string descriptor)
    {
        if (value is null)
        {
            descriptor = "null";
            return true;
        }

        switch (value)
        {
            case string text:
                return DescribeText(typeof(string).FullName!, text, maxValueLength, out descriptor);
            case bool boolean:
                descriptor = typeof(bool).FullName + ":" + (boolean ? "true" : "false");
                return true;
            case byte number:
                descriptor = typeof(byte).FullName + ":" + number.ToString(CultureInfo.InvariantCulture);
                return true;
            case sbyte number:
                descriptor = typeof(sbyte).FullName + ":" + number.ToString(CultureInfo.InvariantCulture);
                return true;
            case short number:
                descriptor = typeof(short).FullName + ":" + number.ToString(CultureInfo.InvariantCulture);
                return true;
            case ushort number:
                descriptor = typeof(ushort).FullName + ":" + number.ToString(CultureInfo.InvariantCulture);
                return true;
            case int number:
                descriptor = typeof(int).FullName + ":" + number.ToString(CultureInfo.InvariantCulture);
                return true;
            case uint number:
                descriptor = typeof(uint).FullName + ":" + number.ToString(CultureInfo.InvariantCulture);
                return true;
            case long number:
                descriptor = typeof(long).FullName + ":" + number.ToString(CultureInfo.InvariantCulture);
                return true;
            case ulong number:
                descriptor = typeof(ulong).FullName + ":" + number.ToString(CultureInfo.InvariantCulture);
                return true;
            case float number:
                descriptor = typeof(float).FullName + ":bits="
                    + BitConverter.ToInt32(BitConverter.GetBytes(number), 0).ToString("X8", CultureInfo.InvariantCulture);
                return true;
            case double number:
                descriptor = typeof(double).FullName + ":bits="
                    + BitConverter.DoubleToInt64Bits(number).ToString("X16", CultureInfo.InvariantCulture);
                return true;
            case decimal number:
                int[] bits = decimal.GetBits(number);
                descriptor = typeof(decimal).FullName + ":bits="
                    + bits[0].ToString("X8", CultureInfo.InvariantCulture)
                    + bits[1].ToString("X8", CultureInfo.InvariantCulture)
                    + bits[2].ToString("X8", CultureInfo.InvariantCulture)
                    + bits[3].ToString("X8", CultureInfo.InvariantCulture);
                return true;
            case char character:
                descriptor = typeof(char).FullName + ":U+"
                    + ((int)character).ToString("X4", CultureInfo.InvariantCulture);
                return true;
            case DateTime dateTime:
                descriptor = typeof(DateTime).FullName + ":binary="
                    + dateTime.ToBinary().ToString(CultureInfo.InvariantCulture);
                return true;
            case DateTimeOffset dateTimeOffset:
                descriptor = typeof(DateTimeOffset).FullName + ":ticks="
                    + dateTimeOffset.Ticks.ToString(CultureInfo.InvariantCulture)
                    + ":offset=" + dateTimeOffset.Offset.Ticks.ToString(CultureInfo.InvariantCulture);
                return true;
            case TimeSpan timeSpan:
                descriptor = typeof(TimeSpan).FullName + ":ticks="
                    + timeSpan.Ticks.ToString(CultureInfo.InvariantCulture);
                return true;
            case Guid guid:
                descriptor = typeof(Guid).FullName + ":" + guid.ToString("D").ToUpperInvariant();
                return true;
            default:
                descriptor = string.Empty;
                return false;
        }
    }

    private static bool DescribeText(string typeName, string text, int maxValueLength, out string descriptor)
    {
        if (maxValueLength > 0 && text.Length > maxValueLength)
        {
            descriptor = typeName
                + "#chars=" + text.Length.ToString(CultureInfo.InvariantCulture)
                + "#sha256=" + Sha256TextHash.HexDigest(text);
            return true;
        }

        descriptor = typeName + ":" + text;
        return true;
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
