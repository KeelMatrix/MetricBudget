// Copyright (c) KeelMatrix

namespace KeelMatrix.MetricBudget.Tests;

/// <summary>
/// Per-test metric names.
/// </summary>
/// <remarks>
/// Instrument publication is process-global, so every test creates meters and instruments with names that no
/// other test can match. Names are derived from the test name plus a unique suffix, and the meters are disposed
/// with the test.
/// </remarks>
internal static class TestNames
{
    internal static string Meter(string testName)
    {
        return "tests.metricbudget." + testName + "." + Guid.NewGuid().ToString("N");
    }

    internal static string Instrument(string testName)
    {
        return "tests.metricbudget." + testName + "." + Guid.NewGuid().ToString("N");
    }
}
