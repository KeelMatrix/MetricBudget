// Copyright (c) KeelMatrix

#if NET8_0_OR_GREATER

using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Globalization;
using Xunit.Abstractions;

namespace KeelMatrix.MetricBudget.Tests;

/// <summary>
/// Resource gate: extreme cardinality stays bounded and canonicalization cost stays representative.
/// </summary>
/// <remarks>
/// These tests run on the modern runtime only, where the measurement numbers are meaningful. They print their
/// numbers so the local validation gate can quote them.
/// </remarks>
public sealed class ResourceTests
{
    private const int ExtremeCombinations = 200_000;

    private readonly ITestOutputHelper output;

    public ResourceTests(ITestOutputHelper output)
    {
        this.output = output;
    }

    [Fact]
    public void ExtremeCardinalityRemainsBounded()
    {
        string meterName = TestNames.Meter(nameof(ExtremeCardinalityRemainsBounded));
        using Meter meter = new Meter(meterName, "1.0.0");
        Counter<long> counter = meter.CreateCounter<long>("requests");

        const int seriesBound = 1_024;
        const int valueBound = 256;
        string payload = new string('v', 64);

        MetricBudgetOptions options = new MetricBudgetOptions
        {
            MaxTrackedSeries = seriesBound,
            MaxTrackedValuesPerTag = valueBound,
        };
        options.ForInstrument(meterName, "requests", budget =>
        {
            budget.MaxObservedSeries = int.MaxValue;
            budget.Tag("tenant").MaxDistinctValues = int.MaxValue;
            budget.Tag("resource").MaxDistinctValues = int.MaxValue;
        });

        using MetricBudgetSession session = MetricBudgetSession.Start(options);

        long before = Measure();
        Stopwatch stopwatch = Stopwatch.StartNew();

        for (int i = 0; i < ExtremeCombinations; i++)
        {
            int tenant = i % 5_000;
            int resource = i / 5_000;
            counter.Add(
                1,
                new KeyValuePair<string, object?>("tenant", payload + "-tenant-" + tenant.ToString(CultureInfo.InvariantCulture)),
                new KeyValuePair<string, object?>("resource", payload + "-resource-" + resource.ToString(CultureInfo.InvariantCulture)));
        }

        stopwatch.Stop();
        long after = Measure();
        MetricBudgetReport report = session.Complete();

        long retainedBytes = Math.Max(0, after - before);
        output.WriteLine(
            "extreme cardinality: combinations={0}; elapsedMs={1}; retainedBytes={2}; observedSeries={3}; untrackedSeriesObservations={4}",
            ExtremeCombinations.ToString(CultureInfo.InvariantCulture),
            stopwatch.ElapsedMilliseconds.ToString(CultureInfo.InvariantCulture),
            retainedBytes.ToString(CultureInfo.InvariantCulture),
            report.ObservedSeriesCount.ToString(CultureInfo.InvariantCulture),
            report.Safety.UntrackedSeriesObservations.ToString(CultureInfo.InvariantCulture));

        Assert.Equal(MetricBudgetOutcome.ObservationIncomplete, report.Outcome);
        Assert.True(report.Safety.SeriesTrackingIncomplete);
        Assert.True(report.Safety.TagValueTrackingIncomplete);
        Assert.Equal(seriesBound, report.ObservedSeriesCount);
        Assert.Equal(ExtremeCombinations - seriesBound, report.Safety.UntrackedSeriesObservations);
        Assert.Equal(ExtremeCombinations, report.TotalMeasurementsObserved);
        Assert.True(report.AccountingIsConsistent);

        // The workload generates 200,000 distinct two-tag combinations with 64-character values. Retaining them
        // would cost well over 100 MB; the safety bounds must keep the session far below that.
        Assert.True(
            retainedBytes < 32L * 1024 * 1024,
            "Sessions must stay bounded under explosive cardinality; retained bytes were "
                + retainedBytes.ToString(CultureInfo.InvariantCulture));
    }

    [Fact]
    public void CanonicalizationCostStaysRepresentativeAtRealisticSeriesCounts()
    {
        string meterName = TestNames.Meter(nameof(CanonicalizationCostStaysRepresentativeAtRealisticSeriesCounts));
        using Meter meter = new Meter(meterName, "1.0.0");
        Counter<long> counter = meter.CreateCounter<long>("http.client.request.duration");

        const int distinctSeries = 20_000;
        const int measurements = 100_000;
        const long allocationBoundBytes = 512L * 1024 * 1024;

        MetricBudgetOptions options = new MetricBudgetOptions
        {
            MaxTrackedSeries = distinctSeries * 2,
            MaxTrackedValuesPerTag = distinctSeries * 2,
        };
        options.ForInstrument(meterName, "http.client.request.duration", budget =>
        {
            budget.MaxObservedSeries = distinctSeries * 2;
            budget.Tag("server.address").MaxDistinctValues = distinctSeries;
            budget.Tag("http.response.status_code").MaxDistinctValues = 16;
        });

        using MetricBudgetSession session = MetricBudgetSession.Start(options);

        // Warm up so the measured window is not dominated by first-use costs. The warm-up repeats the same tag
        // combinations the measured loop produces with a small series cap, so it adds no extra observed series.
        for (int i = 0; i < 1_000; i++)
        {
            int series = i % distinctSeries;
            counter.Add(
                1,
                new KeyValuePair<string, object?>("server.address", "host-" + series.ToString(CultureInfo.InvariantCulture)),
                new KeyValuePair<string, object?>("http.response.status_code", series % 2 == 0 ? 200 : 503));
        }

        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        Stopwatch stopwatch = Stopwatch.StartNew();
        for (int i = 0; i < measurements; i++)
        {
            int series = i % distinctSeries;
            counter.Add(
                1,
                new KeyValuePair<string, object?>("server.address", "host-" + series.ToString(CultureInfo.InvariantCulture)),
                new KeyValuePair<string, object?>("http.response.status_code", series % 2 == 0 ? 200 : 503));
        }

        stopwatch.Stop();
        long allocatedAfter = GC.GetAllocatedBytesForCurrentThread();
        MetricBudgetReport report = session.Complete();

        double microsecondsPerMeasurement = stopwatch.Elapsed.TotalMilliseconds * 1_000 / measurements;
        output.WriteLine(
            "canonicalization: measurements={0}; distinctSeriesGenerated={1}; elapsedMs={2}; microsecondsPerMeasurement={3}; allocatedBytes={4}; allocationBoundBytes={5}; observedSeries={6}",
            measurements.ToString(CultureInfo.InvariantCulture),
            distinctSeries.ToString(CultureInfo.InvariantCulture),
            stopwatch.ElapsedMilliseconds.ToString(CultureInfo.InvariantCulture),
            microsecondsPerMeasurement.ToString("F2", CultureInfo.InvariantCulture),
            (allocatedAfter - allocatedBefore).ToString(CultureInfo.InvariantCulture),
            allocationBoundBytes.ToString(CultureInfo.InvariantCulture),
            report.ObservedSeriesCount.ToString(CultureInfo.InvariantCulture));

        Assert.Equal(MetricBudgetOutcome.Passed, report.Outcome);
        Assert.Equal(distinctSeries, report.ObservedSeriesCount);
        Assert.True(report.AccountingIsConsistent);

        // The pre-optimization shipping path allocated 2,745,842,840 bytes for this same window, including a new
        // 8,192-byte SHA-256 scratch buffer for each of three digests per measurement. This bound leaves generous
        // room for runtime/listener allocation while keeping that regression well outside the acceptable range.
        Assert.True(
            allocatedAfter - allocatedBefore < allocationBoundBytes,
            "Shipping canonicalization allocated "
                + (allocatedAfter - allocatedBefore).ToString(CultureInfo.InvariantCulture)
                + " bytes, above the bound of "
                + allocationBoundBytes.ToString(CultureInfo.InvariantCulture));

        // A generous ceiling: it catches an order-of-magnitude regression without turning a shared build agent into
        // a timing lottery.
        Assert.True(
            microsecondsPerMeasurement < 250,
            "Canonicalization must stay representative; measured "
                + microsecondsPerMeasurement.ToString("F2", CultureInfo.InvariantCulture)
                + " microseconds per measurement.");
    }

    private static long Measure()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        return GC.GetTotalMemory(forceFullCollection: true);
    }
}

#endif
