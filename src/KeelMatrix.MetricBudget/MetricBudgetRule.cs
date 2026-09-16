// Copyright (c) KeelMatrix

namespace KeelMatrix.MetricBudget;

/// <summary>
/// One instrument-selection rule and the budget that applies to it.
/// </summary>
/// <remarks>
/// A rule is created through <see cref="MetricBudgetOptions"/> and is immutable afterwards. An instrument that
/// matches more than one rule is ambiguous and makes the session report invalid configuration instead of silently
/// choosing a budget.
/// </remarks>
public sealed class MetricBudgetRule
{
    internal MetricBudgetRule(InstrumentSelector selector, InstrumentBudget budget)
    {
        Selector = selector;
        Budget = budget;
    }

    /// <summary>
    /// Instruments this rule selects.
    /// </summary>
    public InstrumentSelector Selector { get; }

    /// <summary>
    /// Budget applied to every instrument this rule selects.
    /// </summary>
    public InstrumentBudget Budget { get; }

    /// <summary>
    /// Describes the rule as <c>selector: budget</c>.
    /// </summary>
    /// <returns>A short description.</returns>
    public override string ToString()
    {
        return Selector + ": " + Budget;
    }
}
