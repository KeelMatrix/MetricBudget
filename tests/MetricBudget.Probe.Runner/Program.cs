using System.Diagnostics;
using System.Globalization;
using MetricBudget.Probe.Runner.Probes;

namespace MetricBudget.Probe.Runner;

internal static class Program
{
    private static readonly string[] AllProbes =
    {
        "coverage", "lifecycle", "tags", "isolation", "scale", "parity",
    };

    private static int Main(string[] args)
    {
        try
        {
            return Run(args);
        }
        catch (Exception ex)
        {
            // No failure may escape as an unhandled exception, because that would discard the evidence this run
            // already produced and report nothing to the operator.
            ProbeReport.Line("probeRunComplete = false");
            ProbeReport.Line("probeRunAborted = true");
            ProbeReport.KeyValue("abortReason", ProbeReport.DescribeException(ex));
            return 2;
        }
    }

    private static int Run(string[] args)
    {
        List<string> selected = new List<string>();
        foreach (string argument in args)
        {
            if (argument is "--help" or "-h")
            {
                PrintUsage();
                return 0;
            }

            if (argument is "--list")
            {
                ProbeReport.Line(string.Join(", ", AllProbes));
                return 0;
            }

            if (argument.StartsWith("--probe=", StringComparison.Ordinal))
            {
                string name = argument.Substring("--probe=".Length);
                if (Array.IndexOf(AllProbes, name) < 0)
                {
                    ProbeReport.Line("Unknown probe: " + name);
                    PrintUsage();
                    return 1;
                }

                selected.Add(name);
                continue;
            }

            ProbeReport.Line("Unknown argument: " + argument);
            PrintUsage();
            return 1;
        }

        if (selected.Count == 0)
        {
            selected.AddRange(AllProbes);
        }

        Console.WriteLine("MetricBudget feasibility probe");
        ProbeReport.EnvironmentHeader();

        List<ProbeSectionResult> results = new List<ProbeSectionResult>();
        Stopwatch total = Stopwatch.StartNew();

        foreach (string probe in selected)
        {
            results.Add(RunProbeIsolated(probe));
        }

        total.Stop();

        ProbeReport.Section("phase 0 summary");
        ProbeReport.KeyValue("probesRun", string.Join(", ", selected));
        ProbeReport.KeyValue("totalElapsedMs", total.Elapsed.TotalMilliseconds.ToString("0.0", CultureInfo.InvariantCulture));
        ProbeReport.KeyValue("peakWorkingSetBytes", ProbeReport.CurrentProcessPeakWorkingSetBytes());

        foreach (ProbeSectionResult result in results)
        {
            foreach (ProbeFinding finding in result.Findings)
            {
                ProbeReport.Line(
                    "SUMMARY[" + ProbeReport.FormatVerdict(finding.Verdict) + "] "
                    + result.Name + " :: " + finding.Item + " - " + finding.Rationale);
            }
        }

        int passes = 0;
        int narrows = 0;
        int fails = 0;
        int gates = 0;
        int gateFails = 0;
        int observationFails = 0;
        List<ProbeFinding> gateFailures = new List<ProbeFinding>();
        foreach (ProbeSectionResult result in results)
        {
            foreach (ProbeFinding finding in result.Findings)
            {
                switch (finding.Verdict)
                {
                    case ProbeVerdict.Pass:
                        passes++;
                        break;
                    case ProbeVerdict.Narrow:
                        narrows++;
                        break;
                    default:
                        fails++;
                        break;
                }

                if (finding.IsRunGate)
                {
                    gates++;
                    if (finding.Verdict == ProbeVerdict.Fail)
                    {
                        gateFails++;
                        gateFailures.Add(finding);
                    }
                }
                else if (finding.Verdict == ProbeVerdict.Fail)
                {
                    observationFails++;
                }
            }
        }

        ProbeReport.Section("runner verdict");
        ProbeReport.KeyValue("findingsTotal", passes + narrows + fails);
        ProbeReport.KeyValue("findingsPass", passes);
        ProbeReport.KeyValue("findingsNarrow", narrows);
        ProbeReport.KeyValue("findingsFail", fails);
        ProbeReport.KeyValue("runGates", gates);
        ProbeReport.KeyValue("gateFailures", gateFailures.Count);
        ProbeReport.KeyValue("observationFindings", passes + narrows + fails - gates);
        ProbeReport.KeyValue("observationFails", observationFails);
        ProbeReport.Line(
            "  observation findings record measured platform behavior; a FAIL there is an expected negative result "
            + "and never changes the exit code");

        foreach (ProbeFinding finding in gateFailures)
        {
            ProbeReport.Line("GATE[FAIL] " + finding.Item + " - " + finding.Rationale);
        }

        AssertGateCounting(gates, gateFails);

        bool passed = gateFailures.Count == 0;
        ProbeReport.KeyValue("probeVerdict", passed ? "PASS" : "FAIL");
        ProbeReport.KeyValue("exitCode", passed ? 0 : 1);
        ProbeReport.Line("probeRunComplete = " + ProbeReport.Format(passed));
        return passed ? 0 : 1;
    }

    private static void AssertGateCounting(int gates, int gateFails)
    {
        if (gates <= 0 || gateFails > gates)
        {
            throw new InvalidOperationException(
                "runner gate accounting is inconsistent: gates=" + gates.ToString(CultureInfo.InvariantCulture)
                    + ", gateFails=" + gateFails.ToString(CultureInfo.InvariantCulture));
        }
    }

    /// <summary>
    /// Runs one probe so that a defect in that probe is reported as a named non-zero finding instead of aborting
    /// the run and destroying the evidence later probes would have produced.
    /// </summary>
    private static ProbeSectionResult RunProbeIsolated(string probe)
    {
        try
        {
            return RunProbe(probe);
        }
        catch (Exception ex)
        {
            ProbeSectionResult result = new ProbeSectionResult(probe);
            result.Add(
                "probe '" + probe + "' completed without an unhandled exception",
                ProbeVerdict.Fail,
                "the probe threw instead of reporting a verdict: " + ProbeReport.DescribeException(ex));
            return result;
        }
    }

    private static ProbeSectionResult RunProbe(string probe)
    {
        switch (probe)
        {
            case "coverage":
                return InstrumentCoverageProbe.Run();
            case "lifecycle":
                return InstrumentLifecycleProbe.Run();
            case "tags":
                return TagIdentityProbe.Run();
            case "isolation":
                return ParallelIsolationProbe.Run();
            case "scale":
                return CardinalityScaleProbe.Run();
            case "parity":
                return FrameworkParityProbe.Run();
            default:
                throw new ArgumentException("Unknown probe: " + probe, nameof(probe));
        }
    }

    private static void PrintUsage()
    {
        ProbeReport.Line("Usage: dotnet run --project tests/MetricBudget.Probe.Runner/MetricBudget.Probe.Runner.csproj -c Release");
        ProbeReport.Line("       add --probe=<name> for a single probe; names: " + string.Join(", ", AllProbes));
    }
}
