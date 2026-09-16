// Copyright (c) KeelMatrix

using System.Diagnostics.Metrics;
using KeelMatrix.MetricBudget.Assertions;

namespace KeelMatrix.MetricBudget.Tests;

/// <summary>
/// Diagnostics must stay actionable without disclosing tag or metric values.
/// </summary>
public sealed class DiagnosticsPrivacyTests
{
    private const string SecretValue = "SECRET-TENANT-8f4c1d";
    private const string SecretUrl = "https://internal.example.invalid/customer/42?token=abc";

    [Fact]
    public void DiagnosticTextNamesTagKeysAndCountsButNeverTagValues()
    {
        MetricBudgetReport report = RunHostileWorkload(out string instrumentName, out string meterName);

        string diagnostics = report.ToDiagnosticString();

        Assert.Contains(instrumentName, diagnostics, StringComparison.Ordinal);
        Assert.Contains(meterName, diagnostics, StringComparison.Ordinal);
        Assert.Contains("route", diagnostics, StringComparison.Ordinal);
        Assert.Contains("tenant", diagnostics, StringComparison.Ordinal);
        Assert.Contains("observed", diagnostics, StringComparison.Ordinal);

        Assert.DoesNotContain(SecretValue, diagnostics, StringComparison.Ordinal);
        Assert.DoesNotContain(SecretUrl, diagnostics, StringComparison.Ordinal);
        Assert.DoesNotContain("internal.example.invalid", diagnostics, StringComparison.Ordinal);

        // The measured values themselves are never part of a report either.
        Assert.DoesNotContain("987654321", diagnostics, StringComparison.Ordinal);
    }

    [Fact]
    public void AssertionFailureMessageCarriesTheSamePrivacyBoundary()
    {
        MetricBudgetReport report = RunHostileWorkload(out _, out _);

        MetricBudgetAssertionException exception = Assert.Throws<MetricBudgetAssertionException>(
            () => report.AssertWithinBudget());

        Assert.DoesNotContain(SecretValue, exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(SecretUrl, exception.Message, StringComparison.Ordinal);
        Assert.Contains("MetricBudget observed-cardinality report", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void StructuredViolationsCarryIdentityAndCountsOnly()
    {
        MetricBudgetReport report = RunHostileWorkload(out string instrumentName, out _);

        MetricBudgetViolation violation = report.Violations
            .Single(candidate => candidate.Kind == MetricBudgetViolationKind.TagDistinctValuesBudgetExceeded);

        Assert.Equal("tenant", violation.TagKey);
        Assert.Equal(instrumentName, violation.InstrumentName);
        Assert.NotNull(violation.ObservedCount);
        Assert.NotNull(violation.ConfiguredLimit);
        Assert.DoesNotContain(SecretValue, violation.Description, StringComparison.Ordinal);
    }

    private static MetricBudgetReport RunHostileWorkload(out string instrumentName, out string meterName)
    {
        meterName = TestNames.Meter(nameof(RunHostileWorkload));
        instrumentName = TestNames.Instrument("requests");

        using Meter meter = new Meter(meterName, "1.0.0");
        Counter<long> counter = meter.CreateCounter<long>(instrumentName);

        MetricBudgetOptions options = new MetricBudgetOptions()
            .ForInstrument(meterName, instrumentName, budget =>
            {
                budget.MaxObservedSeries = 100;
                budget.Tag("tenant").MaxDistinctValues = 1;
            });

        using MetricBudgetSession session = MetricBudgetSession.Start(options);
        counter.Add(
            987654321,
            new KeyValuePair<string, object?>("tenant", SecretValue),
            new KeyValuePair<string, object?>("route", SecretUrl));
        counter.Add(
            987654321,
            new KeyValuePair<string, object?>("tenant", SecretValue + "-second"),
            new KeyValuePair<string, object?>("route", "/public"));

        return session.Complete();
    }
}
