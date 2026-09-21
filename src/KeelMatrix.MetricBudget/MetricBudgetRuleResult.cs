// Copyright (c) KeelMatrix

using System.Collections.ObjectModel;

namespace KeelMatrix.MetricBudget;

/// <summary>
/// Whether a configured rule was matched, measured, and accounted without rejected selected identities.
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
    private readonly bool selectionTrackingIncomplete;

    internal MetricBudgetRuleResult(
        MetricBudgetRule rule,
        int ruleIndex,
        InstrumentObservationState state,
        bool selectionTrackingIncomplete,
        IReadOnlyList<MetricBudgetInstrumentResult> instruments)
    {
        Rule = rule;
        RuleIndex = ruleIndex;
        State = state;
        this.selectionTrackingIncomplete = selectionTrackingIncomplete;
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
    /// Results for every selected instrument identity that was retained and enabled, ordered by identity. Rejected
    /// identities are omitted when admission was limited; inspect <see cref="MetricBudgetReport.Outcome"/> and
    /// <see cref="IsWithinBudget"/> for the resulting incomplete verification. Empty when the rule matched no
    /// published instrument or every matching identity was rejected.
    /// </summary>
    public IReadOnlyList<MetricBudgetInstrumentResult> Instruments { get; }

    /// <summary>
    /// Whether the rule selected an instrument population, every selected identity was retained and enabled without
    /// admission loss or selector ambiguity, the retained instruments delivered measurements, every budget was
    /// respected, and tracking was complete.
    /// </summary>
    public bool IsWithinBudget
    {
        get
        {
            if (State != InstrumentObservationState.Observed)
            {
                return false;
            }

            if (selectionTrackingIncomplete)
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
