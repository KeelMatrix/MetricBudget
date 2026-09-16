// Copyright (c) KeelMatrix

namespace KeelMatrix.MetricBudget.Assertions;

/// <summary>
/// Thrown when a metric-budget assertion fails.
/// </summary>
/// <remarks>
/// The exception type belongs to this package rather than to a test framework, so the assertion helpers work in
/// xUnit, NUnit, MSTest, a console harness, or a build script without adding a test-framework dependency. Test
/// frameworks report any exception as a failure.
/// </remarks>
public sealed class MetricBudgetAssertionException : InvalidOperationException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="MetricBudgetAssertionException"/> class.
    /// </summary>
    public MetricBudgetAssertionException()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="MetricBudgetAssertionException"/> class.
    /// </summary>
    /// <param name="message">Actionable failure message, including the diagnostic report.</param>
    public MetricBudgetAssertionException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="MetricBudgetAssertionException"/> class.
    /// </summary>
    /// <param name="message">Actionable failure message, including the diagnostic report.</param>
    /// <param name="innerException">Underlying failure.</param>
    public MetricBudgetAssertionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
