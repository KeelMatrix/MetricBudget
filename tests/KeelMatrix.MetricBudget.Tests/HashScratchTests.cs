// Copyright (c) KeelMatrix

using System.Diagnostics.Metrics;
using System.Reflection;
using KeelMatrix.MetricBudget.Internal;

namespace KeelMatrix.MetricBudget.Tests;

public sealed class HashScratchTests
{
    [Fact]
    public void ReusableHashScratchIsClearedAfterEveryDigestAndSessionStop()
    {
        string shortValue = "short-sensitive-value";
        string multiChunkValue = new string('M', 4096 * 3 + 17);
        string longValue = new string('L', 4096 * 2 + 1);

        _ = Sha256TextHash.HexDigest(shortValue);
        AssertChunkBytesAreClear();

        _ = Sha256TextHash.HexDigest(multiChunkValue);
        AssertChunkBytesAreClear();

        _ = Sha256TextHash.HexDigest(longValue);
        _ = Sha256TextHash.HexDigest("shorter");
        AssertChunkBytesAreClear();

        _ = Sha256TextHash.HexDigest(string.Empty);
        AssertChunkBytesAreClear();

        Assert.Throws<NullReferenceException>(() => Sha256TextHash.HexDigest(null!));
        AssertChunkBytesAreClear();

        string meterName = TestNames.Meter(nameof(ReusableHashScratchIsClearedAfterEveryDigestAndSessionStop));
        using Meter meter = new Meter(meterName, "1.0.0");
        Counter<long> counter = meter.CreateCounter<long>("requests");
        MetricBudgetOptions options = new MetricBudgetOptions()
            .ForInstrument(meterName, "requests", budget => budget.MaxObservedSeries = 1);

        using MetricBudgetSession completedSession = MetricBudgetSession.Start(options);
        counter.Add(1, new KeyValuePair<string, object?>("tag", longValue));
        _ = completedSession.Complete();
        AssertChunkBytesAreClear();
        completedSession.Dispose();
        AssertChunkBytesAreClear();

        using MetricBudgetSession disposedSession = MetricBudgetSession.Start(options);
        counter.Add(1, new KeyValuePair<string, object?>("tag", multiChunkValue));
        disposedSession.Dispose();
        AssertChunkBytesAreClear();
    }

    private static void AssertChunkBytesAreClear()
    {
        FieldInfo field = typeof(Sha256TextHash).GetField(
            "chunkBytes",
            BindingFlags.NonPublic | BindingFlags.Static)!;
        byte[]? scratch = (byte[]?)field.GetValue(null);
        Assert.NotNull(scratch);
        Assert.All(scratch!, value => Assert.Equal(0, value));
    }
}
