// Copyright (c) KeelMatrix

using System.Collections.ObjectModel;

namespace KeelMatrix.MetricBudget;

/// <summary>
/// Whether a configured rule was matched, measured, and accounted.
/// </summary>
public enum InstrumentObservationState
{
    /// <summary>No published instrument matched the rule.</summary>
    NotObserved = 0,

    /// <summary>At least one instrument matched the rule, but none of them delivered a measurement.</summary>
    NoMeasurements = 1,

    /// <summary>At least one matching instrument delivered measurements.</summary>
    Observed = 2,
}

/// <summary>
/// Result of one configured rule for a completed session.
/// </summary>
public sealed class MetricBudgetRuleResult
{
    internal MetricBudgetRuleResult(
        MetricBudgetRule rule,
        int ruleIndex,
        InstrumentObservationState state,
        IReadOnlyList<MetricBudgetInstrumentResult> instruments)
    {
        Rule = rule;
        RuleIndex = ruleIndex;
        State = state;
        MetricBudgetInstrumentResult[] copy = new MetricBudgetInstrumentResult[instruments.Count];
        for (int i = 0; i < copy.Length; i++)
        {
            copy[i] = instruments[i];
        }

        Instruments = new ReadOnlyCollection<MetricBudgetInstrumentResult>(copy);
    }

    /// <summary>
    /// The configured rule this result belongs to.
    /// </summary>
    public MetricBudgetRule Rule { get; }

    /// <summary>
    /// Zero-based position of the rule in <see cref="MetricBudgetOptions.Rules"/>.
    /// </summary>
    public int RuleIndex { get; }

    /// <summary>
    /// Whether the rule selected instruments and whether they delivered measurements.
    /// </summary>
    public InstrumentObservationState State { get; }

    /// <summary>
    /// Results for every instrument identity this rule selected, ordered by identity. Empty when the rule matched no
    /// published instrument.
    /// </summary>
    public IReadOnlyList<MetricBudgetInstrumentResult> Instruments { get; }

    /// <summary>
    /// Whether the rule selected an instrument, that instrument delivered measurements, and every budget was
    /// respected with complete tracking.
    /// </summary>
    public bool IsWithinBudget
    {
        get
        {
            if (State != InstrumentObservationState.Observed)
            {
                return false;
            }

            for (int i = 0; i < Instruments.Count; i++)
            {
                if (!Instruments[i].IsWithinBudget)
                {
                    return false;
                }
            }

            return true;
        }
    }

    /// <summary>
    /// Describes the rule and what it observed.
    /// </summary>
    /// <returns>A short description.</returns>
    public override string ToString()
    {
        return "rule " + (RuleIndex + 1).ToString(System.Globalization.CultureInfo.InvariantCulture)
            + " (" + Rule.Selector + "): " + State
            + ", " + Instruments.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)
            + " instrument(s) matched";
    }
}
