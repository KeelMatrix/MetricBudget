// Copyright (c) KeelMatrix

using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace KeelMatrix.MetricBudget.Internal;

/// <summary>
/// Fixed-size SHA-256 identity of one tag value descriptor or one observed series key.
/// </summary>
/// <remarks>
/// Accounting keys are digests rather than the canonical text so that a session never retains raw tag values in
/// memory, and so that the observed-series set stays a fixed size per series.
/// </remarks>
internal readonly struct Sha256Digest : IEquatable<Sha256Digest>
{
    private readonly ulong first;
    private readonly ulong second;
    private readonly ulong third;
    private readonly ulong fourth;

    private Sha256Digest(ulong first, ulong second, ulong third, ulong fourth)
    {
        this.first = first;
        this.second = second;
        this.third = third;
        this.fourth = fourth;
    }

    internal static Sha256Digest FromHash(byte[] hash)
    {
        return new Sha256Digest(
            ReadUInt64(hash, 0),
            ReadUInt64(hash, 8),
            ReadUInt64(hash, 16),
            ReadUInt64(hash, 24));
    }

    public bool Equals(Sha256Digest other)
    {
        return first == other.first && second == other.second && third == other.third && fourth == other.fourth;
    }

    public override bool Equals(object? obj)
    {
        return obj is Sha256Digest other && Equals(other);
    }

    public override int GetHashCode()
    {
        return unchecked((int)(first ^ (first >> 32) ^ second));
    }

    private static ulong ReadUInt64(byte[] bytes, int offset)
    {
        return ((ulong)bytes[offset] << 56)
            | ((ulong)bytes[offset + 1] << 48)
            | ((ulong)bytes[offset + 2] << 40)
            | ((ulong)bytes[offset + 3] << 32)
            | ((ulong)bytes[offset + 4] << 24)
            | ((ulong)bytes[offset + 5] << 16)
            | ((ulong)bytes[offset + 6] << 8)
            | bytes[offset + 7];
    }
}

/// <summary>
/// Lossless UTF-16-code-unit SHA-256 helper for tag identity work.
/// </summary>
/// <remarks>
/// A hasher is cached per thread because one measurement computes one digest per delivered tag plus one digest for
/// the whole tag set. Long values are hashed in fixed-size chunks so a single pathological value cannot cause a
/// proportional transient allocation. The reusable byte scratch is cleared on every digest exit, including early
/// returns and exceptions, so thread-local storage is not treated as retained accounting state.
/// </remarks>
internal static class Sha256TextHash
{
    private const int ChunkChars = 4096;

    [ThreadStatic]
    private static SHA256? hasher;

    [ThreadStatic]
    private static byte[]? chunkBytes;

    internal static Sha256Digest Digest(string text)
    {
        return Sha256Digest.FromHash(Compute(text));
    }

    /// <summary>
    /// Lowercase hexadecimal digest. Used only inside the documented oversized-value descriptor.
    /// </summary>
    internal static string HexDigest(string text)
    {
        byte[] hash = Compute(text);
        StringBuilder builder = new StringBuilder(hash.Length * 2);
        for (int i = 0; i < hash.Length; i++)
        {
            builder.Append(hash[i].ToString("x2", CultureInfo.InvariantCulture));
        }

        return builder.ToString();
    }

    private static byte[] Compute(string text)
    {
        SHA256 sha = hasher ??= SHA256.Create();
        sha.Initialize();

        byte[] scratch = chunkBytes ??= new byte[ChunkChars * 2];
        try
        {
            int offset = 0;
            while (offset < text.Length)
            {
                int length = Math.Min(ChunkChars, text.Length - offset);
                int byteCount = 0;
                for (int i = 0; i < length; i++)
                {
                    char character = text[offset + i];
                    scratch[byteCount++] = (byte)character;
                    scratch[byteCount++] = (byte)(character >> 8);
                }

                if (offset == 0 && length == text.Length)
                {
                    return sha.ComputeHash(scratch, 0, byteCount);
                }

                sha.TransformBlock(scratch, 0, byteCount, outputBuffer: null, outputOffset: 0);
                offset += length;
            }

            sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
            return sha.Hash ?? Array.Empty<byte>();
        }
        finally
        {
            Array.Clear(scratch, 0, scratch.Length);
        }
    }
}
