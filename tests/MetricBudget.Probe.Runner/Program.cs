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
                selected.Add(argument.Substring("--probe=".Length));
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
            ProbeSectionResult result = RunProbe(probe);
            results.Add(result);
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

        ProbeReport.Line("probeRunComplete = true");
        return 0;
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
