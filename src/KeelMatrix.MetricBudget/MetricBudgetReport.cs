// Copyright (c) KeelMatrix

using System.Globalization;
using System.Text;

namespace KeelMatrix.MetricBudget;

/// <summary>
/// Immutable result of one completed budget verification.
/// </summary>
/// <remarks>
/// <para>
/// A report describes observed cardinality for the exercised workload only. It cannot prove the maximum
/// cardinality production will produce and it is not a cost estimate.
/// </para>
/// <para>
/// The report contains instrument identity, limits, counts, and tag keys. It never enumerates tag values, so
/// printing it in test output or CI logs does not disclose the data those tags carried.
/// </para>
/// </remarks>
public sealed class MetricBudgetReport
{
    private readonly string diagnosticText;

    internal MetricBudgetReport(
        MetricBudgetOutcome outcome,
        IReadOnlyList<MetricBudgetRuleResult> rules,
        IReadOnlyList<MetricBudgetViolation> violations,
        MetricBudgetSafetyReport safety,
        long totalMeasurementsObserved,
        int observedInstrumentCount,
        int observedSeriesCount,
        bool accountingIsConsistent)
    {
        Outcome = outcome;
        Rules = rules;
        Violations = violations;
        Safety = safety;
        TotalMeasurementsObserved = totalMeasurementsObserved;
        ObservedInstrumentCount = observedInstrumentCount;
        ObservedSeriesCount = observedSeriesCount;
        AccountingIsConsistent = accountingIsConsistent;
        diagnosticText = BuildDiagnosticText();
    }

    /// <summary>
    /// Result of this verification.
    /// </summary>
    public MetricBudgetOutcome Outcome { get; }

    /// <summary>
    /// Whether the exercised workload stayed within every configured budget with complete accounting. Equivalent to
    /// <see cref="Outcome"/> being <see cref="MetricBudgetOutcome.Passed"/>.
    /// </summary>
    public bool IsWithinBudget => Outcome == MetricBudgetOutcome.Passed;

    /// <summary>
    /// Whether the session's internal accounting satisfied every invariant, including that every delivered
    /// measurement was accounted exactly once.
    /// </summary>
    /// <remarks>
    /// A <see langword="false"/> value means the session observed an impossible accounting state. It is reported as
    /// an explicit violation and can never present as a pass.
    /// </remarks>
    public bool AccountingIsConsistent { get; }

    /// <summary>
    /// One result per configured rule, in configuration order.
    /// </summary>
    public IReadOnlyList<MetricBudgetRuleResult> Rules { get; }

    /// <summary>
    /// Structured problem records, ordered by rule and instrument identity. Empty when
    /// <see cref="IsWithinBudget"/> is <see langword="true"/>.
    /// </summary>
    public IReadOnlyList<MetricBudgetViolation> Violations { get; }

    /// <summary>
    /// Safety bounds that applied to this session and whether any of them was reached.
    /// </summary>
    public MetricBudgetSafetyReport Safety { get; }

    /// <summary>
    /// Number of configured rules.
    /// </summary>
    public int ConfiguredRuleCount => Rules.Count;

    /// <summary>
    /// Number of measurements delivered by selected instruments.
    /// </summary>
    public long TotalMeasurementsObserved { get; }

    /// <summary>
    /// Number of selected instrument identities that delivered at least one measurement.
    /// </summary>
    public int ObservedInstrumentCount { get; }

    /// <summary>
    /// Sum of tracked distinct observed series across instruments. A lower bound when
    /// <see cref="MetricBudgetSafetyReport.SeriesTrackingIncomplete"/> is <see langword="true"/>.
    /// </summary>
    public int ObservedSeriesCount { get; }

    /// <summary>
    /// Returns an actionable, privacy-safe multi-line report.
    /// </summary>
    /// <remarks>
    /// The text contains meter and instrument identity, configured limits, counts, tag keys, and safety-bound
    /// state. It never contains tag values, metric values, or samples of measured data, which makes it safe to
    /// print from a failing test.
    /// </remarks>
    /// <returns>The diagnostic report text.</returns>
    public string ToDiagnosticString()
    {
        return diagnosticText;
    }

    /// <summary>
    /// Returns a single-line summary containing the outcome and the configured rule count.
    /// </summary>
    /// <returns>A one-line summary.</returns>
    public override string ToString()
    {
        return "MetricBudget " + Outcome
            + ": " + ConfiguredRuleCount.ToString(CultureInfo.InvariantCulture) + " rule(s), "
            + ObservedInstrumentCount.ToString(CultureInfo.InvariantCulture) + " instrument(s) observed, "
            + ObservedSeriesCount.ToString(CultureInfo.InvariantCulture) + " observed series, "
            + TotalMeasurementsObserved.ToString(CultureInfo.InvariantCulture) + " measurements";
    }

    private string BuildDiagnosticText()
    {
        StringBuilder builder = new StringBuilder();

        builder.AppendLine("MetricBudget observed-cardinality report: " + OutcomeDescription(Outcome));
        builder.AppendLine(
            "  rules: " + ConfiguredRuleCount.ToString(CultureInfo.InvariantCulture)
            + "; instruments observed: " + ObservedInstrumentCount.ToString(CultureInfo.InvariantCulture)
            + "; observed series: " + ObservedSeriesCount.ToString(CultureInfo.InvariantCulture)
            + "; measurements: " + TotalMeasurementsObserved.ToString(CultureInfo.InvariantCulture));
        builder.AppendLine(
            "  safety bounds: series " + Safety.MaxTrackedSeries.ToString(CultureInfo.InvariantCulture)
            + ", values per tag " + Safety.MaxTrackedValuesPerTag.ToString(CultureInfo.InvariantCulture)
            + ", tag value length " + Safety.MaxTagValueLength.ToString(CultureInfo.InvariantCulture)
            + "; accounting complete: " + Safety.IsComplete.ToString(CultureInfo.InvariantCulture)
            + "; accounting consistent: " + AccountingIsConsistent.ToString(CultureInfo.InvariantCulture));

        for (int i = 0; i < Rules.Count; i++)
        {
            MetricBudgetRuleResult rule = Rules[i];
            builder.AppendLine(
                "  rule " + (rule.RuleIndex + 1).ToString(CultureInfo.InvariantCulture)
                + ": " + rule.Rule.Selector + " -> " + rule.State);

            if (rule.Instruments.Count == 0)
            {
                builder.AppendLine("    no published instrument matched this rule");
                continue;
            }

            for (int j = 0; j < rule.Instruments.Count; j++)
            {
                MetricBudgetInstrumentResult instrument = rule.Instruments[j];
                builder.AppendLine("    " + instrument);

                if (instrument.SeriesTrackingIncomplete)
                {
                    builder.AppendLine(
                        "      series tracking incomplete: "
                        + instrument.UntrackedSeriesObservations.ToString(CultureInfo.InvariantCulture)
                        + " measurement(s) could not be tracked because the instrument's series safety bound was reached");
                }

                // Tag keys are ordered by observed fan-out so the largest contributor to series growth is first.
                List<MetricBudgetTagResult> tags = new List<MetricBudgetTagResult>(instrument.Tags);
                tags.Sort(static (left, right) =>
                {
                    int byCount = right.ObservedDistinctValueCount.CompareTo(left.ObservedDistinctValueCount);
                    return byCount != 0 ? byCount : string.CompareOrdinal(left.Key ?? string.Empty, right.Key ?? string.Empty);
                });

                for (int k = 0; k < tags.Count; k++)
                {
                    MetricBudgetTagResult tag = tags[k];
                    if (!tag.WasObserved && !tag.IsConfigured)
                    {
                        continue;
                    }

                    builder.Append("      tag " + tag);
                    if (!tag.WasObserved)
                    {
                        builder.Append(" (configured, never delivered by the workload)");
                    }
                    else if (!tag.IsConfigured)
                    {
                        builder.Append(" (no budget configured)");
                    }

                    builder.AppendLine();
                }
            }
        }

        if (Violations.Count > 0)
        {
            builder.AppendLine("  problems:");
            for (int i = 0; i < Violations.Count; i++)
            {
                builder.AppendLine("    - " + Violations[i].Description);
            }
        }

        return builder.ToString();
    }

    private static string OutcomeDescription(MetricBudgetOutcome outcome)
    {
        return outcome switch
        {
            MetricBudgetOutcome.Passed => "PASS (observed workload stayed within every configured budget)",
            MetricBudgetOutcome.Violation => "FAIL (observed budget violation)",
            MetricBudgetOutcome.InvalidConfiguration => "INVALID CONFIGURATION (no verification result)",
            MetricBudgetOutcome.NoMatchingInstrument => "NO MATCHING INSTRUMENT (nothing was verified)",
            MetricBudgetOutcome.NoMeasurementsObserved => "NO MEASUREMENTS OBSERVED (nothing was verified)",
            MetricBudgetOutcome.ObservationIncomplete => "INCOMPLETE (safety bound reached; counts are lower bounds)",
            _ => outcome.ToString(),
        };
    }
}
