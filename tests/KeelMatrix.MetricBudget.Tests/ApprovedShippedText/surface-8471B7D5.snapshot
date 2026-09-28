// Copyright (c) KeelMatrix

using System.Diagnostics.Metrics;
using KeelMatrix.MetricBudget;
using KeelMatrix.MetricBudget.Assertions;

// Isolated package-consumer smoke test.
//
// This project references the built NuGet package from a local package source only. It defines its own meter and
// instruments, runs one workload that must stay within budget, and one workload that must exceed it. Both the
// passing and the failing path have to behave as documented for the smoke test to succeed.

Environment.SetEnvironmentVariable("KEELMATRIX_NO_TELEMETRY", "1");

int exitCode = 0;

exitCode += RunPassingBudget();
exitCode += RunFailingBudget();
exitCode += RunRuleLevelIncompleteAdmission();
exitCode += RunFocusedObservationIncompleteAdmission();
exitCode += RunFocusedObservationIncompleteTagValueTracking();
exitCode += RunDateTimeIdentityBudget();
exitCode += RunConflictPrecedence();
exitCode += RunStaticMetadataIdentityDiscriminators();

Console.WriteLine(exitCode == 0
    ? "PACKAGE CONSUMER SMOKE: PASS"
    : "PACKAGE CONSUMER SMOKE: FAIL");

return exitCode;

static int RunPassingBudget()
{
    Console.WriteLine("--- passing budget ---");

    using Meter meter = new Meter("Smoke.Consumer", "1.0.0");
    Counter<long> requests = meter.CreateCounter<long>("requests");

    MetricBudgetOptions options = new MetricBudgetOptions()
        .ForInstrument("Smoke.Consumer", "requests", budget =>
        {
            budget.MaxObservedSeries = 3;
            budget.Tag("route").MaxDistinctValues = 3;
        });

    using MetricBudgetSession session = MetricBudgetSession.Start(options);
    requests.Add(1, new KeyValuePair<string, object?>("route", "/a"));
    requests.Add(1, new KeyValuePair<string, object?>("route", "/b"));
    requests.Add(1, new KeyValuePair<string, object?>("route", "/a"));

    MetricBudgetReport report = session.Complete();
    Console.WriteLine(report.ToDiagnosticString());

    try
    {
        _ = report.AssertWithinBudget();
    }
    catch (MetricBudgetAssertionException exception)
    {
        Console.WriteLine("UNEXPECTED: passing workload failed the budget: " + exception.Message);
        return 1;
    }

    bool expected = report.Outcome == MetricBudgetOutcome.Passed
        && report.TotalMeasurementsObserved == 3
        && report.ObservedSeriesCount == 2;

    if (!expected)
    {
        Console.WriteLine("UNEXPECTED: passing workload reported " + report);
        return 1;
    }

    Console.WriteLine("passing workload reported " + report);
    return 0;
}

static int RunFailingBudget()
{
    Console.WriteLine("--- failing budget ---");

    using Meter meter = new Meter("Smoke.Consumer.Failing", "1.0.0");
    Counter<long> requests = meter.CreateCounter<long>("requests");

    MetricBudgetOptions options = new MetricBudgetOptions()
        .ForInstrument("Smoke.Consumer.Failing", "requests", budget =>
        {
            budget.MaxObservedSeries = 1;
            budget.Tag("tenant").MaxDistinctValues = 1;
        });

    using MetricBudgetSession session = MetricBudgetSession.Start(options);
    requests.Add(1, new KeyValuePair<string, object?>("tenant", "tenant-a"));
    requests.Add(1, new KeyValuePair<string, object?>("tenant", "tenant-b"));

    MetricBudgetReport report = session.Complete();
    Console.WriteLine(report.ToDiagnosticString());

    if (report.Outcome != MetricBudgetOutcome.Violation)
    {
        Console.WriteLine("UNEXPECTED: over-budget workload reported " + report.Outcome);
        return 1;
    }

    try
    {
        _ = report.AssertWithinBudget();
        Console.WriteLine("UNEXPECTED: over-budget workload did not fail the assertion.");
        return 1;
    }
    catch (MetricBudgetAssertionException exception)
    {
        Console.WriteLine("over-budget workload failed the assertion as expected.");
        Console.WriteLine("first line: " + exception.Message.Split('\n')[0]);
    }

    return 0;
}

static int RunRuleLevelIncompleteAdmission()
{
    Console.WriteLine("--- rule-level incomplete admission ---");

    using Meter meter = new Meter("Smoke.Consumer.RuleAdmission", "1.0.0");
    MetricBudgetOptions options = new MetricBudgetOptions
    {
        MaxTrackedInstrumentIdentities = 1,
    };
    options.ForMeter("Smoke.Consumer.RuleAdmission", budget => budget.MaxObservedSeries = 1);

    using MetricBudgetSession session = MetricBudgetSession.Start(options);
    Counter<long> first = meter.CreateCounter<long>("first");
    first.Add(1);
    Counter<long> second = meter.CreateCounter<long>("second");
    second.Add(1, new KeyValuePair<string, object?>("tenant", "tenant-a"));
    second.Add(1, new KeyValuePair<string, object?>("tenant", "tenant-b"));

    MetricBudgetReport report = session.Complete();
    Console.WriteLine(report.ToDiagnosticString());

    bool expected = report.Outcome == MetricBudgetOutcome.ObservationIncomplete
        && report.Rules.Count == 1
        && report.Rules[0].Instruments.Count == 1
        && report.Rules[0].Instruments[0].InstrumentName == "first"
        && !report.Rules[0].IsWithinBudget;

    if (!expected)
    {
        Console.WriteLine("UNEXPECTED: rule-level admission loss was not surfaced: " + report);
        return 1;
    }

    Console.WriteLine("rule-level admission loss failed the rule result as expected.");
    return 0;
}

static int RunFocusedObservationIncompleteAdmission()
{
    Console.WriteLine("--- focused observation incomplete admission ---");

    const string meterName = "Smoke.Consumer.FocusedObservationAdmission";
    using Meter firstMeter = new Meter(meterName, "1.0.0");
    using Meter secondMeter = new Meter(meterName, "1.0.0");
    Counter<long> retained = firstMeter.CreateCounter<long>("requests", unit: "milliseconds", description: "first");
    Counter<long> rejected = secondMeter.CreateCounter<long>("requests", unit: "seconds", description: "second");
    MetricBudgetOptions options = new MetricBudgetOptions
    {
        MaxTrackedInstrumentIdentities = 1,
    };
    options.ForInstrument(meterName, "requests", budget => budget.MaxObservedSeries = 1);

    using MetricBudgetSession session = MetricBudgetSession.Start(options);
    retained.Add(1);
    rejected.Add(1);

    MetricBudgetReport report = session.Complete();
    MetricBudgetInstrumentResult retainedResult = report.Rules[0].Instruments.Single();
    Console.WriteLine(report.ToDiagnosticString());

    if (report.Outcome != MetricBudgetOutcome.ObservationIncomplete
        || !retainedResult.WasObserved
        || !retainedResult.InstrumentTrackingIncomplete)
    {
        Console.WriteLine("UNEXPECTED: focused observation admission loss was not surfaced: " + report);
        return 1;
    }

    int failures = 0;
    try
    {
        _ = report.AssertInstrumentObserved(meterName, "requests");
    }
    catch (MetricBudgetAssertionException)
    {
        failures++;
    }

    try
    {
        _ = report.AssertInstrumentObserved(meterName, "requests", retainedResult.IdentityDiscriminator);
    }
    catch (MetricBudgetAssertionException)
    {
        failures++;
    }

    if (failures != 2)
    {
        Console.WriteLine("UNEXPECTED: focused observation assertions did not fail closed: " + report);
        return 1;
    }

    Console.WriteLine("both focused observation overloads failed closed for incomplete identity admission.");
    return 0;
}

static int RunFocusedObservationIncompleteTagValueTracking()
{
    Console.WriteLine("--- focused observation incomplete tag-value tracking ---");

    const string meterName = "Smoke.Consumer.FocusedObservationTagValues";
    using Meter meter = new Meter(meterName, "1.0.0");
    Counter<long> requests = meter.CreateCounter<long>("requests");
    MetricBudgetOptions options = new MetricBudgetOptions
    {
        MaxTrackedValuesPerTag = 1,
    };
    options.ForInstrument(meterName, "requests", budget =>
    {
        budget.MaxObservedSeries = 2;
        budget.Tag("tenant").MaxDistinctValues = 2;
    });

    using MetricBudgetSession session = MetricBudgetSession.Start(options);
    requests.Add(1, new KeyValuePair<string, object?>("tenant", "tenant-a"));
    requests.Add(1, new KeyValuePair<string, object?>("tenant", "tenant-b"));

    MetricBudgetReport report = session.Complete();
    MetricBudgetInstrumentResult result = report.Rules[0].Instruments.Single();
    Console.WriteLine(report.ToDiagnosticString());

    if (report.Outcome != MetricBudgetOutcome.ObservationIncomplete
        || !result.WasObserved
        || !result.Tags.Single().ValueTrackingIncomplete)
    {
        Console.WriteLine("UNEXPECTED: focused observation tag-value loss was not surfaced: " + report);
        return 1;
    }

    int failures = 0;
    try
    {
        _ = report.AssertInstrumentObserved(meterName, "requests");
    }
    catch (MetricBudgetAssertionException)
    {
        failures++;
    }

    try
    {
        _ = report.AssertInstrumentObserved(meterName, "requests", result.IdentityDiscriminator);
    }
    catch (MetricBudgetAssertionException)
    {
        failures++;
    }

    if (failures != 2)
    {
        Console.WriteLine("UNEXPECTED: focused observation tag-value assertions did not fail closed: " + report);
        return 1;
    }

    Console.WriteLine("both focused observation overloads failed closed for incomplete tag-value tracking.");
    return 0;
}

static int RunDateTimeIdentityBudget()
{
    Console.WriteLine("--- DateTime identity budget ---");

    using Meter meter = new Meter("Smoke.Consumer.DateTime", "1.0.0");
    Counter<long> requests = meter.CreateCounter<long>("requests");
    MetricBudgetOptions options = new MetricBudgetOptions()
        .ForInstrument("Smoke.Consumer.DateTime", "requests", budget =>
        {
            budget.MaxObservedSeries = 1;
            budget.Tag("stamp").MaxDistinctValues = 1;
        });

    DateTime springForwardWallClock = new DateTime(2010, 3, 14, 2, 30, 0, DateTimeKind.Unspecified);
    DateTime afterSpringForwardWallClock = new DateTime(2010, 3, 14, 3, 30, 0, DateTimeKind.Unspecified);
    string timeZoneId = Environment.GetEnvironmentVariable("METRICBUDGET_CONTROLLED_TIME_ZONE")
        ?? (OperatingSystem.IsWindows() ? "Pacific Standard Time" : "America/Los_Angeles");
    TimeZoneInfo controlledTimeZone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
    // The controlled zone is a test oracle for the DST wall-clock witness. The process-local zone is deliberately
    // untouched because the shipping identity contract preserves DateTime Ticks and Kind instead of consulting it.
    if (!controlledTimeZone.IsInvalidTime(springForwardWallClock) || controlledTimeZone.IsInvalidTime(afterSpringForwardWallClock))
    {
        Console.WriteLine("UNEXPECTED: controlled DST witness was not an invalid spring-forward gap in " + timeZoneId);
        return 1;
    }

    Console.WriteLine("controlled DST timezone = " + controlledTimeZone.Id + " (no process-wide timezone change)");
    DateTime springForwardGap = DateTime.SpecifyKind(springForwardWallClock, DateTimeKind.Local);
    DateTime afterSpringForwardGap = DateTime.SpecifyKind(afterSpringForwardWallClock, DateTimeKind.Local);
    using MetricBudgetSession session = MetricBudgetSession.Start(options);
    requests.Add(1, new KeyValuePair<string, object?>("stamp", springForwardGap));
    requests.Add(1, new KeyValuePair<string, object?>("stamp", afterSpringForwardGap));

    MetricBudgetReport report = session.Complete();
    MetricBudgetInstrumentResult instrument = report.Rules[0].Instruments[0];
    MetricBudgetTagResult tag = instrument.Tags[0];
    Console.WriteLine(report.ToDiagnosticString());

    bool expected = report.Outcome == MetricBudgetOutcome.Violation
        && report.TotalMeasurementsObserved == 2
        && report.ObservedSeriesCount == 2
        && report.Rules[0].IsWithinBudget == false
        && instrument.ObservedSeriesCount == 2
        && instrument.IsWithinBudget == false
        && tag.ObservedDistinctValueCount == 2
        && tag.IsWithinBudget == false
        && report.IsWithinBudget == false;
    if (!expected)
    {
        Console.WriteLine("UNEXPECTED: DateTime identity budget collapsed distinct wall-clock values: " + report);
        return 1;
    }

    Console.WriteLine("DateTime identity preserved two distinct values and violated both configured limits.");
    return 0;
}

static int RunConflictPrecedence()
{
    Console.WriteLine("--- conflict precedence ---");

    using Meter meter = new Meter("Smoke.Consumer.Conflict", "1.0.0");
    Counter<long> requests = meter.CreateCounter<long>("requests");
    MetricBudgetOptions options = new MetricBudgetOptions
    {
        MaxTrackedConflicts = 1,
    };
    options.ForMeter("Smoke.Consumer.Conflict", budget => budget.MaxObservedSeries = 10);
    options.ForInstrument("Smoke.Consumer.Conflict", "requests", budget => budget.MaxObservedSeries = 10);

    using MetricBudgetSession session = MetricBudgetSession.Start(options);
    requests.Add(1);
    MetricBudgetReport report = session.Complete();
    Console.WriteLine(report.ToDiagnosticString());

    bool expected = report.Outcome == MetricBudgetOutcome.InvalidConfiguration
        && report.IsWithinBudget == false
        && report.Safety.ConflictTrackingIncomplete
        && report.Violations.Any(violation => violation.Kind == MetricBudgetViolationKind.ConfigurationInvalid)
        && report.Rules.All(rule => rule.IsWithinBudget == false);
    if (!expected)
    {
        Console.WriteLine("UNEXPECTED: bounded conflict detail loss was not reported as invalid configuration: " + report);
        return 1;
    }

    Console.WriteLine("conflict precedence stayed invalid when detailed conflict retention was exhausted.");
    return 0;
}

static int RunStaticMetadataIdentityDiscriminators()
{
    Console.WriteLine("--- static metadata identity discriminators ---");

    const string meterName = "Smoke.Consumer.StaticMetadataIdentity";
    using Meter firstMeter = new Meter(new MeterOptions(meterName)
    {
        Version = "1.0.0",
        Tags = new[] { new KeyValuePair<string, object?>("stream", "first") },
    });
    using Meter secondMeter = new Meter(new MeterOptions(meterName)
    {
        Version = "1.0.0",
        Tags = new[] { new KeyValuePair<string, object?>("stream", "second") },
    });
    Counter<long> first = firstMeter.CreateCounter<long>("requests");
    Counter<long> second = secondMeter.CreateCounter<long>("requests");
    MetricBudgetOptions options = new MetricBudgetOptions();
    options.ForInstrument(meterName, "requests", budget => budget.MaxObservedSeries = 1);

    using MetricBudgetSession session = MetricBudgetSession.Start(options);
    first.Add(1, new KeyValuePair<string, object?>("route", "/first"));
    second.Add(1, new KeyValuePair<string, object?>("route", "/second-a"));
    second.Add(1, new KeyValuePair<string, object?>("route", "/second-b"));
    MetricBudgetReport report = session.Complete();
    IReadOnlyList<MetricBudgetInstrumentResult> instruments = report.Rules[0].Instruments;
    MetricBudgetInstrumentResult firstResult = instruments.Single(instrument => instrument.ObservedSeriesCount == 1);
    MetricBudgetInstrumentResult secondResult = instruments.Single(instrument => instrument.ObservedSeriesCount == 2);

    if (instruments.Count != 2
        || firstResult.IdentityDiscriminator == secondResult.IdentityDiscriminator
        || firstResult.MeasurementCount != 1
        || secondResult.MeasurementCount != 2)
    {
        Console.WriteLine("UNEXPECTED: same-name static-metadata streams were not retained separately: " + report);
        return 1;
    }

    try
    {
        _ = report.AssertObservedSeriesAtMost(
            meterName,
            "requests",
            firstResult.IdentityDiscriminator,
            1);
    }
    catch (MetricBudgetAssertionException exception)
    {
        Console.WriteLine("UNEXPECTED: first discriminator did not select its within-budget stream: " + exception.Message);
        return 1;
    }

    try
    {
        _ = report.AssertObservedSeriesAtMost(
            meterName,
            "requests",
            secondResult.IdentityDiscriminator,
            1);
        Console.WriteLine("UNEXPECTED: second discriminator did not retain its series-budget violation.");
        return 1;
    }
    catch (MetricBudgetAssertionException)
    {
        Console.WriteLine("both same-name streams remained separately selectable by discriminator.");
    }

    const string failureMeterName = "Smoke.Consumer.StaticMetadataFailure";
    using Meter failureMeter = new Meter(failureMeterName, "1.0.0");
    Counter<long> failureCounter = failureMeter.CreateCounter<long>("requests", description: "too-long");
    MetricBudgetOptions failureOptions = new MetricBudgetOptions
    {
        MaxStaticMetadataTextLength = 1,
    };
    failureOptions.ForInstrument(failureMeterName, "requests", budget => budget.MaxObservedSeries = 1);
    using MetricBudgetSession failureSession = MetricBudgetSession.Start(failureOptions);
    failureCounter.Add(1);
    MetricBudgetReport failureReport = failureSession.Complete();

    if (failureReport.Safety.StaticMetadataFailures.Count != 1
        || failureReport.Safety.StaticMetadataFailures[0].Kind != MetricBudgetStaticMetadataFailureKind.TextLength)
    {
        Console.WriteLine("UNEXPECTED: static metadata rejection was not reported through Safety.StaticMetadataFailures.");
        return 1;
    }

    Console.WriteLine("static metadata rejection was reported through Safety.StaticMetadataFailures.");
    return 0;
}
