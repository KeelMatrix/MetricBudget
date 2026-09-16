// Copyright (c) KeelMatrix

namespace KeelMatrix.MetricBudget;

/// <summary>
/// Thrown when a session is started with options that cannot describe a meaningful verification.
/// </summary>
/// <remarks>
/// Invalid configuration fails before any measurement is observed so the mistake is reported immediately and
/// actionably rather than silently producing a passing session that verified nothing. Ambiguity discovered while
/// instruments are published is reported by <see cref="MetricBudgetReport.Outcome"/> instead, because the
/// conflicting instruments may not exist in every run.
/// </remarks>
public sealed class MetricBudgetConfigurationException : InvalidOperationException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="MetricBudgetConfigurationException"/> class.
    /// </summary>
    public MetricBudgetConfigurationException()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="MetricBudgetConfigurationException"/> class.
    /// </summary>
    /// <param name="message">Description of the invalid configuration.</param>
    public MetricBudgetConfigurationException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="MetricBudgetConfigurationException"/> class.
    /// </summary>
    /// <param name="message">Description of the invalid configuration.</param>
    /// <param name="innerException">Underlying failure.</param>
    public MetricBudgetConfigurationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
