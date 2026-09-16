using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;

namespace MetricBudget.Probe.Runner;

internal enum ProbeVerdict
{
    Pass,
    Narrow,
    Fail,
}

internal sealed class ProbeFinding
{
    public ProbeFinding(string item, ProbeVerdict verdict, string rationale)
    {
        Item = item;
        Verdict = verdict;
        Rationale = rationale;
    }

    public string Item { get; }

    public ProbeVerdict Verdict { get; }

    public string Rationale { get; }
}

internal sealed class ProbeSectionResult
{
    public ProbeSectionResult(string name)
    {
        Name = name;
    }

    public string Name { get; }

    public List<ProbeFinding> Findings { get; } = new List<ProbeFinding>();

    public void Add(string item, ProbeVerdict verdict, string rationale)
    {
        Findings.Add(new ProbeFinding(item, verdict, rationale));
        ProbeReport.Line("VERDICT[" + ProbeReport.FormatVerdict(verdict) + "] " + item + " - " + rationale);
    }
}

internal readonly struct ProbeMeasurement
{
    public ProbeMeasurement(
        double elapsedMs,
        long threadAllocatedBytes,
        long totalAllocatedBytes,
        long workingSetBefore,
        long workingSetAfter,
        long managedHeapBytes)
    {
        ElapsedMs = elapsedMs;
        ThreadAllocatedBytes = threadAllocatedBytes;
        TotalAllocatedBytes = totalAllocatedBytes;
        WorkingSetBefore = workingSetBefore;
        WorkingSetAfter = workingSetAfter;
        ManagedHeapBytes = managedHeapBytes;
    }

    public double ElapsedMs { get; }

    public long ThreadAllocatedBytes { get; }

    public long TotalAllocatedBytes { get; }

    public long WorkingSetBefore { get; }

    public long WorkingSetAfter { get; }

    public long ManagedHeapBytes { get; }

    public string Describe()
    {
        return "elapsedMs=" + ElapsedMs.ToString("0.0", CultureInfo.InvariantCulture)
            + "; allocatedThreadBytes=" + ThreadAllocatedBytes.ToString(CultureInfo.InvariantCulture)
            + "; allocatedTotalBytes=" + TotalAllocatedBytes.ToString(CultureInfo.InvariantCulture)
            + "; managedHeapBytes=" + ManagedHeapBytes.ToString(CultureInfo.InvariantCulture)
            + "; workingSetBeforeBytes=" + WorkingSetBefore.ToString(CultureInfo.InvariantCulture)
            + "; workingSetAfterBytes=" + WorkingSetAfter.ToString(CultureInfo.InvariantCulture);
    }
}

internal static class ProbeReport
{
    public static void Section(string title)
    {
        Console.WriteLine();
        Console.WriteLine("=== " + title + " ===");
    }

    public static void Line(string text) => Console.WriteLine(text);

    public static void KeyValue(string key, object? value) => Console.WriteLine("  " + key + " = " + Format(value));

    public static string Format(object? value)
    {
        if (value is null)
        {
            return "<null>";
        }

        if (value is bool boolean)
        {
            return boolean ? "true" : "false";
        }

        if (value is double number)
        {
            return number.ToString("0.###", CultureInfo.InvariantCulture);
        }

        if (value is IFormattable formattable)
        {
            return formattable.ToString(null, CultureInfo.InvariantCulture) ?? "<null>";
        }

        return value.ToString() ?? "<null>";
    }

    public static string FormatVerdict(ProbeVerdict verdict)
    {
        switch (verdict)
        {
            case ProbeVerdict.Pass:
                return "PASS";
            case ProbeVerdict.Narrow:
                return "NARROW";
            default:
                return "FAIL";
        }
    }

    public static void EnvironmentHeader()
    {
        Section("environment");
        KeyValue("utc", DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
        KeyValue("runtime", RuntimeInformation.FrameworkDescription);
        KeyValue("os", RuntimeInformation.OSDescription);
        KeyValue("osArchitecture", RuntimeInformation.OSArchitecture);
        KeyValue("processArchitecture", RuntimeInformation.ProcessArchitecture);
        KeyValue("processorCount", Environment.ProcessorCount);
        KeyValue("serverGarbageCollection", System.Runtime.GCSettings.IsServerGC);
        KeyValue("currentCulture", CultureInfo.CurrentCulture.Name);
        KeyValue("workingSetBytes", Environment.WorkingSet);

        System.Reflection.Assembly diagnosticSource = typeof(System.Diagnostics.Metrics.MeterListener).Assembly;
        KeyValue("diagnosticSourceAssembly", diagnosticSource.GetName().Name + " " + diagnosticSource.GetName().Version);
        KeyValue("diagnosticSourceLocation", diagnosticSource.Location);
    }

    public static ProbeMeasurement Measure(Action action)
    {
        long threadBefore = GC.GetAllocatedBytesForCurrentThread();
        long totalBefore = GC.GetTotalAllocatedBytes(true);
        long workingSetBefore = Environment.WorkingSet;

        Stopwatch stopwatch = Stopwatch.StartNew();
        action();
        stopwatch.Stop();

        long managedHeap = GC.GetTotalMemory(true);

        return new ProbeMeasurement(
            stopwatch.Elapsed.TotalMilliseconds,
            GC.GetAllocatedBytesForCurrentThread() - threadBefore,
            GC.GetTotalAllocatedBytes(true) - totalBefore,
            workingSetBefore,
            Environment.WorkingSet,
            managedHeap);
    }

    public static long CurrentProcessPeakWorkingSetBytes()
    {
        using (Process process = Process.GetCurrentProcess())
        {
            return process.PeakWorkingSet64;
        }
    }
}
