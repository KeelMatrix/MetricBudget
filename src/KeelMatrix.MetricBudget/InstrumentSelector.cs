// Copyright (c) KeelMatrix

namespace KeelMatrix.MetricBudget;

/// <summary>
/// Selects the meters and instruments a budget rule applies to.
/// </summary>
/// <remarks>
/// <para>
/// Matching is exact and ordinal: metric names are case-sensitive in <c>System.Diagnostics.Metrics</c>, and this
/// selector does not normalize, wildcard, or partially match them.
/// </para>
/// <para>
/// A selector names a meter, an instrument, or both. A rule that names only an instrument applies to every meter
/// that publishes that instrument name, and each meter version keeps its own accounting. A rule that names only a
/// meter applies to every instrument that meter publishes.
/// </para>
/// <para>
/// Instrument publication is process-global, so a selector can also match instruments created by unrelated code
/// in the same process. Prefer naming the meter and the instrument together when a process runs several libraries.
/// </para>
/// </remarks>
public sealed class InstrumentSelector
{
    private InstrumentSelector(string? meterName, string? instrumentName)
    {
        MeterName = meterName;
        InstrumentName = instrumentName;
    }

    /// <summary>
    /// Creates a selector that matches one instrument name in any meter.
    /// </summary>
    /// <param name="instrumentName">Exact instrument name.</param>
    /// <returns>The selector.</returns>
    /// <exception cref="ArgumentException"><paramref name="instrumentName"/> is null, empty, or white space.</exception>
    public static InstrumentSelector Instrument(string instrumentName)
    {
        return new InstrumentSelector(meterName: null, Validate(instrumentName, nameof(instrumentName)));
    }

    /// <summary>
    /// Creates a selector that matches one instrument name in one meter, in every version of that meter.
    /// </summary>
    /// <param name="meterName">Exact meter name.</param>
    /// <param name="instrumentName">Exact instrument name.</param>
    /// <returns>The selector.</returns>
    /// <exception cref="ArgumentException">A name is null, empty, or white space.</exception>
    public static InstrumentSelector InstrumentInMeter(string meterName, string instrumentName)
    {
        return new InstrumentSelector(
            Validate(meterName, nameof(meterName)),
            Validate(instrumentName, nameof(instrumentName)));
    }

    /// <summary>
    /// Creates a selector that matches every instrument published by one meter, in every version of that meter.
    /// </summary>
    /// <param name="meterName">Exact meter name.</param>
    /// <returns>The selector.</returns>
    /// <exception cref="ArgumentException"><paramref name="meterName"/> is null, empty, or white space.</exception>
    public static InstrumentSelector Meter(string meterName)
    {
        return new InstrumentSelector(Validate(meterName, nameof(meterName)), instrumentName: null);
    }

    /// <summary>
    /// Meter name this selector requires, or <see langword="null"/> when any meter is accepted.
    /// </summary>
    public string? MeterName { get; }

    /// <summary>
    /// Instrument name this selector requires, or <see langword="null"/> when any instrument is accepted.
    /// </summary>
    public string? InstrumentName { get; }

    /// <summary>
    /// Reports whether this selector matches one published instrument identity.
    /// </summary>
    /// <param name="meterName">Published meter name.</param>
    /// <param name="instrumentName">Published instrument name.</param>
    /// <returns><see langword="true"/> when the identity is selected.</returns>
    /// <exception cref="ArgumentNullException">A supplied name is null.</exception>
    public bool Matches(string meterName, string instrumentName)
    {
        if (meterName is null)
        {
            throw new ArgumentNullException(nameof(meterName));
        }

        if (instrumentName is null)
        {
            throw new ArgumentNullException(nameof(instrumentName));
        }

        return MatchesCore(meterName, instrumentName);
    }

    /// <summary>
    /// Developer-facing description of what this selector matches.
    /// </summary>
    /// <returns>A short description.</returns>
    public override string ToString()
    {
        if (MeterName is null)
        {
            return "instrument \"" + InstrumentName + "\" in any meter";
        }

        return InstrumentName is null
            ? "meter \"" + MeterName + "\""
            : "instrument \"" + InstrumentName + "\" in meter \"" + MeterName + "\"";
    }

    internal bool Matches(in Internal.InstrumentIdentity identity)
    {
        return MatchesCore(identity.MeterName, identity.InstrumentName);
    }

    private bool MatchesCore(string meterName, string instrumentName)
    {
        if (MeterName is not null && !string.Equals(MeterName, meterName, StringComparison.Ordinal))
        {
            return false;
        }

        return InstrumentName is null || string.Equals(InstrumentName, instrumentName, StringComparison.Ordinal);
    }

    private static string Validate(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Metric and instrument names must not be null, empty, or white space. Names are matched exactly.", parameterName);
        }

        return value;
    }
}
