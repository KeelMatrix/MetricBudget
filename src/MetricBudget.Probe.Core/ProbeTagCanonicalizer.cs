using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace MetricBudget.Probe;

/// <summary>
/// Candidate canonicalization for an observed metric series.
/// </summary>
/// <remarks>
/// Identity rule:
/// <list type="number">
/// <item>Every delivered tag becomes one entry, and an entry is a key field, a field separator, and a value
/// field.</item>
/// <item>Key field: <c>{length}:{key}</c> for a delivered key, or the bare marker <c>null</c> when the key is
/// <see langword="null"/>. The marker is unambiguous because every length-prefixed field starts with an ASCII
/// digit, so no delivered key can produce it.</item>
/// <item>Value field: always <c>{length}:{descriptor}</c>.</item>
/// <item>The value descriptor is <c>{CLR type full name}:{invariant culture text}</c>. The CLR type is part of
/// identity, so <c>int 1</c> and <c>string "1"</c> are different series.</item>
/// <item>A <see langword="null"/> value has the descriptor text <c>null</c>, so its value field is
/// <c>4:null</c> and it cannot collide with the string value <c>"null"</c>.</item>
/// <item>Descriptors longer than the configured bound are replaced by a stable
/// <c>{type}#chars={length}#sha256={hex}</c> form so that one pathological value cannot inflate the tracked key.</item>
/// <item>Entries are sorted with ordinal string comparison and joined with U+001F. Fields are self-delimiting by
/// their length prefix, so a value that contains a separator character is still recorded verbatim.</item>
/// <item>Duplicate keys are retained, so a tag set is a sorted multiset, not a set.</item>
/// </list>
/// The resulting key is order-independent, culture-independent, and stable across processes.
/// </remarks>
internal static class ProbeTagCanonicalizer
{
    public const int DefaultMaxDescriptorLength = 512;

    /// <summary>Bare key-field marker for a delivered <see langword="null"/> tag key.</summary>
    public const string NullKeyMarker = "null";

    private const char EntrySeparator = '\u001F';
    private const char FieldSeparator = '\u001E';
    private const int HashChunkChars = 4096;

    public static string CreateSeriesKey(
        KeyValuePair<string, object?>[] tags,
        int maxDescriptorLength = DefaultMaxDescriptorLength)
    {
        if (tags is null || tags.Length == 0)
        {
            return string.Empty;
        }

        string[] entries = new string[tags.Length];
        for (int i = 0; i < tags.Length; i++)
        {
            entries[i] = EncodeEntry(tags[i].Key, DescribeValue(tags[i].Value, maxDescriptorLength));
        }

        Array.Sort(entries, StringComparer.Ordinal);

        StringBuilder builder = new StringBuilder();
        for (int i = 0; i < entries.Length; i++)
        {
            if (i > 0)
            {
                builder.Append(EntrySeparator);
            }

            builder.Append(entries[i]);
        }

        return builder.ToString();
    }

    /// <summary>
    /// Identity key field for one delivered tag key. This is also the grouping key used for per-tag
    /// distinct-value accounting, so a <see langword="null"/> key and the literal key <c>"null"</c> are counted
    /// separately.
    /// </summary>
    public static string EncodeKeyField(string? key) => key is null ? NullKeyMarker : EncodeField(key);

    public static string DescribeValue(object? value, int maxDescriptorLength = DefaultMaxDescriptorLength)
    {
        if (value is null)
        {
            return "null";
        }

        Type type = value.GetType();
        string typeName = type.FullName ?? type.Name;

        if (value is string text)
        {
            if (maxDescriptorLength > 0 && text.Length > maxDescriptorLength)
            {
                return typeName
                    + "#chars=" + text.Length.ToString(CultureInfo.InvariantCulture)
                    + "#sha256=" + Sha256Hex(text);
            }

            return typeName + ":" + text;
        }

        string descriptor = value is IFormattable formattable
            ? typeName + ":" + (formattable.ToString(null, CultureInfo.InvariantCulture) ?? string.Empty)
            : typeName + ":" + (value.ToString() ?? string.Empty);

        if (maxDescriptorLength > 0 && descriptor.Length > maxDescriptorLength)
        {
            return typeName
                + "#chars=" + descriptor.Length.ToString(CultureInfo.InvariantCulture)
                + "#sha256=" + Sha256Hex(descriptor);
        }

        return descriptor;
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
    /// Stable digest for oversized descriptors. The text is hashed in fixed-size chunks so the transient
    /// allocation stays bounded even for multi-megabyte values.
    /// </summary>
    private static string Sha256Hex(string text)
    {
        using (SHA256 sha = SHA256.Create())
        {
            char[] charBuffer = new char[HashChunkChars];
            byte[] byteBuffer = new byte[HashChunkChars * 4];
            int offset = 0;
            while (offset < text.Length)
            {
                int length = Math.Min(HashChunkChars, text.Length - offset);
                if (offset + length < text.Length && char.IsHighSurrogate(charBuffer[length - 1]))
                {
                    length--;
                }

                text.CopyTo(offset, charBuffer, 0, length);
                int byteCount = Encoding.UTF8.GetByteCount(charBuffer, 0, length);
                Encoding.UTF8.GetBytes(charBuffer, 0, length, byteBuffer, 0);
                sha.TransformBlock(byteBuffer, 0, byteCount, null, 0);
                offset += length;
            }

            sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
            byte[] hash = sha.Hash ?? Array.Empty<byte>();

            StringBuilder builder = new StringBuilder(hash.Length * 2);
            for (int i = 0; i < hash.Length; i++)
            {
                builder.Append(hash[i].ToString("x2", CultureInfo.InvariantCulture));
            }

            return builder.ToString();
        }
    }
}
