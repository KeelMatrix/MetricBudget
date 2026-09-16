// Copyright (c) KeelMatrix

using Xunit;

// Instrument publication is process-global and the telemetry sink is process-wide, so the suite runs its test
// classes sequentially. Concurrency coverage is explicit inside ConcurrentAccountingTests, which drives its own
// parallel workload against one session.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
