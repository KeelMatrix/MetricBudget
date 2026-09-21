// Copyright (c) KeelMatrix

using System.Globalization;

namespace KeelMatrix.MetricBudget.Assertions;

/// <summary>
/// Assertion helpers for <see cref="MetricBudgetReport"/>.
/// </summary>
/// <remarks>
/// <para>
/// The helpers take no dependency on a test framework. They throw
/// <see cref="MetricBudgetAssertionException"/>, whose message is the privacy-safe diagnostic report, so a failure
/// in any framework names the instrument, the breached budget, the observed count, and the configured limit
/// without printing tag values.
/// </para>
/// <para>
/// Focused budget helpers fail closed when their target delivered no measurements or its accounting was incomplete.
/// A name-only focused check also fails when selected identity admission loss makes that name ambiguous, while
/// unrelated instruments' incomplete accounting does not invalidate a target that was fully tracked.
/// </para>
/// <para>
/// This namespace is separate from the core API so the core surface stays about observation and budgeting rather
/// than assertion style.
/// </para>
/// </remarks>
public static class MetricBudgetReportAssertions
{
    /// <summary>
    /// Asserts that the exercised workload stayed within every configured budget.
    /// </summary>
    /// <param name="report">Report to assert against.</param>
    /// <returns>The same report, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="report"/> is null.</exception>
    /// <exception cref="MetricBudgetAssertionException">
    /// The outcome is not <see cref="MetricBudgetOutcome.Passed"/>, including when nothing was observed, the
    /// configuration was rejected, or a safety bound was reached.
    /// </exception>
    public static MetricBudgetReport AssertWithinBudget(this MetricBudgetReport report)
    {
        if (report is null)
        {
            throw new ArgumentNullException(nameof(report));
        }

        if (report.IsWithinBudget)
        {
            return report;
        }

        throw new MetricBudgetAssertionException(
            "Expected the exercised workload to stay within every configured observed-cardinality budget, but the "
            + "verification outcome was " + report.Outcome + "." + Environment.NewLine
            + report.ToDiagnosticString());
    }

    /// <summary>
    /// Asserts that a verification ended with a specific outcome.
    /// </summary>
    /// <param name="report">Report to assert against.</param>
    /// <param name="expectedOutcome">Expected outcome.</param>
    /// <returns>The same report, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="report"/> is null.</exception>
    /// <exception cref="MetricBudgetAssertionException">The outcome differs.</exception>
    public static MetricBudgetReport AssertOutcome(this MetricBudgetReport report, MetricBudgetOutcome expectedOutcome)
    {
        if (report is null)
        {
            throw new ArgumentNullException(nameof(report));
        }

        if (report.Outcome == expectedOutcome)
        {
            return report;
        }

        throw new MetricBudgetAssertionException(
            "Expected verification outcome " + expectedOutcome + ", but the report reported " + report.Outcome + "."
            + Environment.NewLine
            + report.ToDiagnosticString());
    }

    /// <summary>
    /// Asserts that one instrument identity was observed with at least one measurement.
    /// </summary>
    /// <param name="report">Report to assert against.</param>
    /// <param name="meterName">Exact meter name.</param>
    /// <param name="instrumentName">Exact instrument name.</param>
    /// <returns>The same report, for chaining.</returns>
    /// <exception cref="ArgumentNullException">A parameter is null.</exception>
    /// <exception cref="MetricBudgetAssertionException">The instrument was not observed with measurements.</exception>
    public static MetricBudgetReport AssertInstrumentObserved(
        this MetricBudgetReport report,
        string meterName,
        string instrumentName)
    {
        if (report is null)
        {
            throw new ArgumentNullException(nameof(report));
        }

        MetricBudgetInstrumentResult? instrument = FindInstrument(report, meterName, instrumentName);
        if (instrument is not null && instrument.WasObserved)
        {
            return report;
        }

        throw new MetricBudgetAssertionException(
            "Expected instrument \"" + instrumentName + "\" in meter \"" + meterName
            + "\" to deliver at least one measurement, but "
            + (instrument is null ? "it was not selected." : "it delivered none.") + Environment.NewLine
            + report.ToDiagnosticString());
    }

    /// <summary>
    /// Asserts that one instrument identity stayed within a specific observed-series limit.
    /// </summary>
    /// <param name="report">Report to assert against.</param>
    /// <param name="meterName">Exact meter name.</param>
    /// <param name="instrumentName">Exact instrument name.</param>
    /// <param name="maxObservedSeries">Maximum distinct observed series allowed.</param>
    /// <returns>The same report, for chaining.</returns>
    /// <exception cref="ArgumentNullException">A parameter is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxObservedSeries"/> is negative.</exception>
    /// <exception cref="MetricBudgetAssertionException">The instrument delivered no measurements, the observed count exceeds the limit, or tracking was incomplete.</exception>
    public static MetricBudgetReport AssertObservedSeriesAtMost(
        this MetricBudgetReport report,
        string meterName,
        string instrumentName,
        int maxObservedSeries)
    {
        if (report is null)
        {
            throw new ArgumentNullException(nameof(report));
        }

        if (maxObservedSeries < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxObservedSeries));
        }

        MetricBudgetInstrumentResult? instrument = FindInstrument(report, meterName, instrumentName);
        if (instrument is not null
            && instrument.WasObserved
            && !InstrumentTrackingIncomplete(instrument)
            && instrument.ObservedSeriesCount <= maxObservedSeries)
        {
            return report;
        }

        throw new MetricBudgetAssertionException(
            "Expected instrument \"" + instrumentName + "\" in meter \"" + meterName + "\" to stay at or below "
            + maxObservedSeries.ToString(CultureInfo.InvariantCulture) + " observed series, but "
            + (instrument is null
                ? "it was not selected."
                : !instrument.WasObserved
                    ? "it delivered no measurements."
                : instrument.ObservedSeriesCount.ToString(CultureInfo.InvariantCulture)
                    + " were observed"
                    + (InstrumentTrackingIncomplete(instrument) ? " and tracking was incomplete." : "."))
            + Environment.NewLine
            + report.ToDiagnosticString());
    }

    /// <summary>
    /// Asserts that one tag key stayed within a specific distinct-value limit for one instrument identity.
    /// </summary>
    /// <param name="report">Report to assert against.</param>
    /// <param name="meterName">Exact meter name.</param>
    /// <param name="instrumentName">Exact instrument name.</param>
    /// <param name="tagKey">Exact tag key.</param>
    /// <param name="maxDistinctValues">Maximum distinct values allowed for the tag key.</param>
    /// <returns>The same report, for chaining.</returns>
    /// <exception cref="ArgumentNullException">A parameter is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxDistinctValues"/> is negative.</exception>
    /// <exception cref="MetricBudgetAssertionException">The observed count exceeds the limit, the tag was not observed, or tracking was incomplete.</exception>
    public static MetricBudgetReport AssertTagDistinctValuesAtMost(
        this MetricBudgetReport report,
        string meterName,
        string instrumentName,
        string tagKey,
        int maxDistinctValues)
    {
        if (report is null)
        {
            throw new ArgumentNullException(nameof(report));
        }

        if (tagKey is null)
        {
            throw new ArgumentNullException(nameof(tagKey));
        }

        if (maxDistinctValues < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxDistinctValues));
        }

        MetricBudgetInstrumentResult? instrument = FindInstrument(report, meterName, instrumentName);
        MetricBudgetTagResult? tag = FindTag(instrument, tagKey);
        if (tag is not null
            && tag.WasObserved
            && instrument is not null
            && !InstrumentTrackingIncomplete(instrument)
            && !TagTrackingIncomplete(tag)
            && tag.ObservedDistinctValueCount <= maxDistinctValues)
        {
            return report;
        }

        bool trackingIncomplete = instrument is not null
            && (InstrumentTrackingIncomplete(instrument) || (tag is not null && TagTrackingIncomplete(tag)));
        string observed = tag is null || !tag.WasObserved
            ? "the workload never delivered that tag key"
            : tag.ObservedDistinctValueCount.ToString(CultureInfo.InvariantCulture)
                + " distinct values were observed"
                + (trackingIncomplete ? " and tracking was incomplete" : string.Empty);

        throw new MetricBudgetAssertionException(
            "Expected tag \"" + tagKey + "\" on instrument \"" + instrumentName + "\" in meter \"" + meterName
            + "\" to stay at or below " + maxDistinctValues.ToString(CultureInfo.InvariantCulture)
            + " observed distinct values, but " + observed + "." + Environment.NewLine
            + report.ToDiagnosticString());
    }

    private static bool InstrumentTrackingIncomplete(MetricBudgetInstrumentResult instrument)
    {
        return instrument.SeriesTrackingIncomplete
            || instrument.TagSetTrackingIncomplete
            || instrument.TagKeyTrackingIncomplete
            || instrument.InstrumentTrackingIncomplete;
    }

    private static bool TagTrackingIncomplete(MetricBudgetTagResult tag)
    {
        return tag.ValueTrackingIncomplete
            || tag.SeriesTrackingIncomplete
            || tag.TagSetTrackingIncomplete
            || tag.TagKeyTrackingIncomplete
            || tag.InstrumentTrackingIncomplete;
    }

    private static MetricBudgetInstrumentResult? FindInstrument(
        MetricBudgetReport report,
        string meterName,
        string instrumentName)
    {
        if (meterName is null)
        {
            throw new ArgumentNullException(nameof(meterName));
        }

        if (instrumentName is null)
        {
            throw new ArgumentNullException(nameof(instrumentName));
        }

        MetricBudgetInstrumentResult? match = null;
        List<string>? ambiguous = null;
        for (int i = 0; i < report.Rules.Count; i++)
        {
            IReadOnlyList<MetricBudgetInstrumentResult> instruments = report.Rules[i].Instruments;
            for (int j = 0; j < instruments.Count; j++)
            {
                MetricBudgetInstrumentResult candidate = instruments[j];
                if (string.Equals(candidate.MeterName, meterName, StringComparison.Ordinal)
                    && string.Equals(candidate.InstrumentName, instrumentName, StringComparison.Ordinal))
                {
                    if (match is null)
                    {
                        match = candidate;
                    }
                    else
                    {
                        ambiguous ??= new List<string>();
                        if (ambiguous.Count == 0)
                        {
                            ambiguous.Add(DescribeIdentity(match));
                        }

                        ambiguous.Add(DescribeIdentity(candidate));
                    }
                }
            }
        }

        if (ambiguous is not null)
        {
            throw new MetricBudgetAssertionException(
                "The focused assertion for meter \"" + meterName + "\" and instrument \"" + instrumentName
                + "\" is ambiguous. It matched multiple instrument identities: "
                + string.Join(", ", ambiguous)
                + ". Inspect the report and select the full meter version and instrument kind before asserting."
                + Environment.NewLine
                + report.ToDiagnosticString());
        }

        return match;
    }

    private static string DescribeIdentity(MetricBudgetInstrumentResult instrument)
    {
        return "meter version " + (instrument.MeterVersion ?? "<none>")
            + ", kind " + instrument.InstrumentKind;
    }

    private static MetricBudgetTagResult? FindTag(MetricBudgetInstrumentResult? instrument, string tagKey)
    {
        if (instrument is null)
        {
            return null;
        }

        for (int i = 0; i < instrument.Tags.Count; i++)
        {
            if (string.Equals(instrument.Tags[i].Key, tagKey, StringComparison.Ordinal))
            {
                return instrument.Tags[i];
            }
        }

        return null;
    }
}
