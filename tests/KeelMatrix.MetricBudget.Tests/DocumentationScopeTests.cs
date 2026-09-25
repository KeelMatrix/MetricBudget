// Copyright (c) KeelMatrix

using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Xml.Linq;

namespace KeelMatrix.MetricBudget.Tests;

/// <summary>
/// Guards every declared shipped text surface with a closed, exact approved snapshot.
/// </summary>
public sealed class DocumentationScopeTests
{
    private const string SnapshotDirectory = "tests/KeelMatrix.MetricBudget.Tests/ApprovedShippedText";
    private const string ApprovalEnvironmentVariable =
        "KEELMATRIX_METRICBUDGET_APPROVE_DOCUMENTATION_SNAPSHOTS";

    private const string ApprovalCommand =
        "$env:KEELMATRIX_METRICBUDGET_APPROVE_DOCUMENTATION_SNAPSHOTS='1'; "
        + "dotnet test tests/KeelMatrix.MetricBudget.Tests/KeelMatrix.MetricBudget.Tests.csproj "
        + "-c Release --no-build --filter FullyQualifiedName~DocumentationScopeTests";

    private const string PackageConsumerPathspec = "tests/KeelMatrix.MetricBudget.PackageConsumer/**";

    private const string RuntimeDiagnosticPassedSurface = "MetricBudgetReport.ToDiagnosticString(): Passed";
    private const string RuntimeDiagnosticViolationSurface = "MetricBudgetReport.ToDiagnosticString(): Violation";
    private const string RuntimeDiagnosticInvalidConfigurationSurface =
        "MetricBudgetReport.ToDiagnosticString(): InvalidConfiguration";
    private const string RuntimeDiagnosticNoMatchingInstrumentSurface =
        "MetricBudgetReport.ToDiagnosticString(): NoMatchingInstrument";
    private const string RuntimeDiagnosticNoMeasurementsObservedSurface =
        "MetricBudgetReport.ToDiagnosticString(): NoMeasurementsObserved";
    private const string RuntimeDiagnosticObservationIncompleteSurface =
        "MetricBudgetReport.ToDiagnosticString(): ObservationIncomplete";

    private static readonly RuntimeDiagnosticScenario[] RuntimeDiagnosticScenarios =
    {
        new RuntimeDiagnosticScenario(RuntimeDiagnosticPassedSurface, "surface-runtime-diagnostic-passed.snapshot"),
        new RuntimeDiagnosticScenario(RuntimeDiagnosticViolationSurface, "surface-runtime-diagnostic-violation.snapshot"),
        new RuntimeDiagnosticScenario(
            RuntimeDiagnosticInvalidConfigurationSurface,
            "surface-runtime-diagnostic-invalid-configuration.snapshot"),
        new RuntimeDiagnosticScenario(
            RuntimeDiagnosticNoMatchingInstrumentSurface,
            "surface-runtime-diagnostic-no-matching-instrument.snapshot"),
        new RuntimeDiagnosticScenario(
            RuntimeDiagnosticNoMeasurementsObservedSurface,
            "surface-runtime-diagnostic-no-measurements-observed.snapshot"),
        new RuntimeDiagnosticScenario(
            RuntimeDiagnosticObservationIncompleteSurface,
            "surface-runtime-diagnostic-observation-incomplete.snapshot"),
    };

    private static readonly char[] NulSeparator = { '\0' };

    private const string OptionsSeriesMember = "P:KeelMatrix.MetricBudget.MetricBudgetOptions.MaxTrackedSeries";

    private const string OptionsTagValuesMember =
        "P:KeelMatrix.MetricBudget.MetricBudgetOptions.MaxTrackedValuesPerTag";

    private const string DefaultSeriesMember =
        "F:KeelMatrix.MetricBudget.MetricBudgetOptions.DefaultMaxTrackedSeries";

    private const string DefaultTagValuesMember =
        "F:KeelMatrix.MetricBudget.MetricBudgetOptions.DefaultMaxTrackedValuesPerTag";

    private const string InstrumentUntrackedSeriesMember =
        "P:KeelMatrix.MetricBudget.MetricBudgetInstrumentResult.UntrackedSeriesObservations";

    private const string InstrumentSeriesIncompleteMember =
        "P:KeelMatrix.MetricBudget.MetricBudgetInstrumentResult.SeriesTrackingIncomplete";

    private const string SafetySeriesMember =
        "P:KeelMatrix.MetricBudget.MetricBudgetSafetyReport.MaxTrackedSeries";

    private const string SafetyTagValuesMember =
        "P:KeelMatrix.MetricBudget.MetricBudgetSafetyReport.MaxTrackedValuesPerTag";

    private const string TagValuesIncompleteMember =
        "P:KeelMatrix.MetricBudget.MetricBudgetTagResult.ValueTrackingIncomplete";

    [Fact]
    public void DeclaredTextSurfacesMatchApprovedSnapshots()
    {
        IReadOnlyList<Surface> surfaces = DocumentationSurfaceFiles();
        AssertSnapshotSet(surfaces);

        foreach (Surface surface in surfaces)
        {
            AssertApprovedSnapshot(surface, SurfaceText(surface));
        }
    }

    [Fact]
    public void SeriesBoundXmlDocumentationStatesThePerInstrumentScopeOnly()
    {
        foreach (Surface surface in DocumentationSurfaceFiles()
            .Where(surface => surface.Kind == SurfaceKind.Xml))
        {
            XDocument documentation = XDocument.Load(surface.Path!);
            Assert.Contains("per instrument identity", documentation.ToString(), StringComparison.OrdinalIgnoreCase);
            Assert.Contains("matched instrument identities", documentation.ToString(), StringComparison.OrdinalIgnoreCase);
            AssertSummaryAssertsPerInstrumentScope(documentation, DefaultSeriesMember);
            AssertSummaryAssertsPerInstrumentScope(documentation, DefaultTagValuesMember);
            AssertSummaryAssertsPerInstrumentScope(documentation, InstrumentUntrackedSeriesMember);
            AssertSummaryAssertsPerInstrumentScope(documentation, InstrumentSeriesIncompleteMember);
            AssertSummaryAssertsPerInstrumentScope(documentation, OptionsSeriesMember);
            AssertSummaryAssertsPerInstrumentScope(documentation, OptionsTagValuesMember);
            AssertSummaryAssertsPerInstrumentScope(documentation, SafetySeriesMember);
            AssertSummaryAssertsPerInstrumentScope(documentation, SafetyTagValuesMember);
            AssertSummaryAssertsPerInstrumentScope(documentation, TagValuesIncompleteMember);
        }
    }

    [Fact]
    public void DiagnosticSeriesBoundNamesTheInstrumentScope()
    {
        string diagnostic = CreateCanonicalDiagnostic(RuntimeDiagnosticObservationIncompleteSurface);

        Assert.Contains(
            "because the series safety bound for the instrument identity was reached",
            diagnostic,
            StringComparison.Ordinal);
        Assert.DoesNotContain("session series bound", diagnostic, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("session safety bound", diagnostic, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("session reached its series safety bound", diagnostic, StringComparison.OrdinalIgnoreCase);
    }

    private static void AssertSummaryAssertsPerInstrumentScope(XDocument documentation, string memberName)
    {
        XElement member = documentation
            .Descendants("member")
            .Single(element => string.Equals((string?)element.Attribute("name"), memberName, StringComparison.Ordinal));

        string summary = member.Element("summary")?.Value ?? string.Empty;
        Assert.Contains("instrument identity", summary, StringComparison.OrdinalIgnoreCase);
    }

    private static void AssertSnapshotSet(IReadOnlyList<Surface> surfaces)
    {
        HashSet<string> declaredSnapshots = new HashSet<string>(
            surfaces.Select(surface => surface.SnapshotRelativePath),
            StringComparer.Ordinal);
        HashSet<string> actualSnapshots = new HashSet<string>(SnapshotFiles(), StringComparer.Ordinal);
        string[] orphanSnapshots = actualSnapshots
            .Where(snapshot => !declaredSnapshots.Contains(snapshot))
            .OrderBy(snapshot => snapshot, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            orphanSnapshots.Length == 0,
            "Approved shipped-text snapshot orphan(s):\n- "
                + string.Join("\n- ", orphanSnapshots)
                + "\nRemove the orphan deliberately, then rerun the focused guard. To approve deliberately, run:\n"
                + ApprovalCommand);

        string[] missingSurfaces = surfaces
            .Where(surface => !actualSnapshots.Contains(surface.SnapshotRelativePath))
            .Select(surface => surface.Identifier)
            .OrderBy(surface => surface, StringComparer.Ordinal)
            .ToArray();

        if (missingSurfaces.Length == 0)
        {
            return;
        }

        if (ApprovalEnabled())
        {
            return;
        }

        Assert.Fail(
            "Declared surface(s) without an approved shipped-text snapshot:\n- "
                + string.Join("\n- ", missingSurfaces)
                + "\nTo approve deliberately, run:\n" + ApprovalCommand);
    }

    private static void AssertApprovedSnapshot(Surface surface, string actualText)
    {
        string snapshotPath = SnapshotPath(surface);
        string normalizedActual = NormalizeText(actualText);
        if (!File.Exists(snapshotPath))
        {
            if (ApprovalEnabled())
            {
                WriteSnapshot(snapshotPath, normalizedActual);
                return;
            }

            Assert.Fail(
                "Declared surface '" + surface.Identifier + "' has no approved snapshot. To approve deliberately, run:\n"
                    + ApprovalCommand);
            return;
        }

        string normalizedApproved = NormalizeText(File.ReadAllText(snapshotPath));
        if (string.Equals(normalizedActual, normalizedApproved, StringComparison.Ordinal))
        {
            return;
        }

        if (ApprovalEnabled())
        {
            WriteSnapshot(snapshotPath, normalizedActual);
            return;
        }

        Assert.Fail(SnapshotMismatchMessage(surface, normalizedApproved, normalizedActual));
    }

    private static string SnapshotMismatchMessage(Surface surface, string approved, string actual)
    {
        string[] approvedLines = approved.Split('\n');
        string[] actualLines = actual.Split('\n');
        int differingLine = 0;
        int commonLineCount = Math.Min(approvedLines.Length, actualLines.Length);
        while (differingLine < commonLineCount
            && string.Equals(approvedLines[differingLine], actualLines[differingLine], StringComparison.Ordinal))
        {
            differingLine++;
        }

        string approvedLine = differingLine < approvedLines.Length
            ? approvedLines[differingLine]
            : "<missing>";
        string actualLine = differingLine < actualLines.Length
            ? actualLines[differingLine]
            : "<missing>";

        return "Approved shipped-text snapshot mismatch for surface '" + surface.Identifier
            + "'. First differing line " + (differingLine + 1) + ":\n"
            + "- approved: " + ShortLine(approvedLine) + "\n"
            + "+ actual:   " + ShortLine(actualLine) + "\n"
            + "Snapshot: " + Path.Combine(SnapshotDirectory, surface.SnapshotRelativePath) + "\n"
            + "To approve deliberately, run:\n" + ApprovalCommand;
    }

    private static string ShortLine(string line)
    {
        const int maxLength = 160;
        if (line.Length <= maxLength)
        {
            return line.Length == 0 ? "<empty>" : line;
        }

        return line.Remove(maxLength - 3) + "...";
    }

    private static bool ApprovalEnabled()
    {
        return string.Equals(
            Environment.GetEnvironmentVariable(ApprovalEnvironmentVariable),
            "1",
            StringComparison.Ordinal);
    }

    private static void WriteSnapshot(string snapshotPath, string normalizedText)
    {
        string? directory = Path.GetDirectoryName(snapshotPath);
        if (directory is not null)
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(snapshotPath, normalizedText, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private static string[] SnapshotFiles()
    {
        string root = SnapshotRoot();
        if (!Directory.Exists(root))
        {
            return Array.Empty<string>();
        }

        return Directory.GetFiles(root, "*", SearchOption.AllDirectories)
            .Select(path => RelativePath(root, path))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
    }

    private static string SnapshotPath(Surface surface)
    {
        return Path.Combine(
            SnapshotRoot(),
            surface.SnapshotRelativePath.Replace('/', Path.DirectorySeparatorChar));
    }

    private static string SnapshotRoot()
    {
        return Path.Combine(RepositoryRoot(), SnapshotDirectory.Replace('/', Path.DirectorySeparatorChar));
    }

    private static List<Surface> DocumentationSurfaceFiles()
    {
        string repositoryRoot = RepositoryRoot();
        List<Surface> surfaces = new List<Surface>();

        HashSet<string> markdownPaths = new HashSet<string>(StringComparer.Ordinal);
        foreach (string relativePath in TrackedFiles("*.md"))
        {
            if (string.Equals(relativePath, "CHANGELOG.md", StringComparison.Ordinal))
            {
                continue;
            }

            markdownPaths.Add(relativePath);
            surfaces.Add(new Surface(
                relativePath,
                Path.Combine(repositoryRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)),
                SurfaceKind.Markdown,
                SnapshotRelativePath(relativePath)));
        }

        foreach (string relativePath in TrackedFiles("samples/**", PackageConsumerPathspec))
        {
            if (markdownPaths.Contains(relativePath))
            {
                continue;
            }

            string absolutePath = Path.Combine(repositoryRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (IsUtf8TextSurfaceFile(absolutePath))
            {
                surfaces.Add(new Surface(
                    relativePath,
                    absolutePath,
                    SurfaceKind.TrackedText,
                    SnapshotRelativePath(relativePath)));
            }
        }

        foreach (string relativePath in new[]
        {
            "src/KeelMatrix.MetricBudget/bin/Release/net8.0/KeelMatrix.MetricBudget.xml",
            "src/KeelMatrix.MetricBudget/bin/Release/netstandard2.0/KeelMatrix.MetricBudget.xml",
        })
        {
            string absolutePath = Path.Combine(repositoryRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(absolutePath), "Declared generated XML surface is missing: " + relativePath);
            surfaces.Add(new Surface(
                relativePath,
                absolutePath,
                SurfaceKind.Xml,
                SnapshotRelativePath(relativePath)));
        }

        foreach (RuntimeDiagnosticScenario scenario in RuntimeDiagnosticScenarios)
        {
            surfaces.Add(new Surface(
                scenario.Identifier,
                null,
                SurfaceKind.RuntimeDiagnostic,
                scenario.SnapshotRelativePath));
        }

        return surfaces;
    }

    private static string SurfaceText(Surface surface)
    {
        return surface.Kind == SurfaceKind.RuntimeDiagnostic
            ? CreateCanonicalDiagnostic(surface.Identifier)
            : File.ReadAllText(surface.Path!);
    }

    private static string SnapshotRelativePath(string surfacePath)
    {
        const uint offsetBasis = 2166136261;
        const uint prime = 16777619;
        uint hash = offsetBasis;
        foreach (char value in surfacePath)
        {
            hash ^= value;
            hash *= prime;
        }

        return "surface-" + hash.ToString("X8", CultureInfo.InvariantCulture) + ".snapshot";
    }

    private static string CreateCanonicalDiagnostic(string surfaceIdentifier)
    {
        return surfaceIdentifier switch
        {
            RuntimeDiagnosticPassedSurface => CreatePassedDiagnostic(),
            RuntimeDiagnosticViolationSurface => CreateViolationDiagnostic(),
            RuntimeDiagnosticInvalidConfigurationSurface => CreateInvalidConfigurationDiagnostic(),
            RuntimeDiagnosticNoMatchingInstrumentSurface => CreateNoMatchingInstrumentDiagnostic(),
            RuntimeDiagnosticNoMeasurementsObservedSurface => CreateNoMeasurementsObservedDiagnostic(),
            RuntimeDiagnosticObservationIncompleteSurface => CreateObservationIncompleteDiagnostic(),
            _ => throw new InvalidOperationException("Unknown runtime diagnostic surface: " + surfaceIdentifier),
        };
    }

    private static string CreatePassedDiagnostic()
    {
        const string meterName = "tests.metricbudget.documentation-scope.passed";
        using Meter meter = new Meter(meterName, "1.0.0");
        Counter<long> counter = meter.CreateCounter<long>("requests");

        MetricBudgetOptions options = new MetricBudgetOptions();
        options.ForInstrument(meterName, "requests", budget => budget.MaxObservedSeries = 4);

        using MetricBudgetSession session = MetricBudgetSession.Start(options);
        counter.Add(1);
        return session.Complete().ToDiagnosticString();
    }

    private static string CreateViolationDiagnostic()
    {
        const string meterName = "tests.metricbudget.documentation-scope.violation";
        using Meter meter = new Meter(meterName, "1.0.0");
        Counter<long> counter = meter.CreateCounter<long>("requests");

        MetricBudgetOptions options = new MetricBudgetOptions();
        options.ForInstrument(meterName, "requests", budget => budget.MaxObservedSeries = 1);

        using MetricBudgetSession session = MetricBudgetSession.Start(options);
        counter.Add(1, new KeyValuePair<string, object?>("route", "/a"));
        counter.Add(1, new KeyValuePair<string, object?>("route", "/b"));
        return session.Complete().ToDiagnosticString();
    }

    private static string CreateInvalidConfigurationDiagnostic()
    {
        const string meterName = "tests.metricbudget.documentation-scope.invalid-configuration";
        using Meter meter = new Meter(meterName, "1.0.0");
        Counter<long> counter = meter.CreateCounter<long>("requests");

        MetricBudgetOptions options = new MetricBudgetOptions()
            .ForMeter(meterName, budget => budget.MaxObservedSeries = 1)
            .ForInstrument(meterName, "requests", budget => budget.MaxObservedSeries = 2);

        using MetricBudgetSession session = MetricBudgetSession.Start(options);
        counter.Add(1);
        return session.Complete().ToDiagnosticString();
    }

    private static string CreateNoMatchingInstrumentDiagnostic()
    {
        const string meterName = "tests.metricbudget.documentation-scope.no-matching-instrument";
        using Meter meter = new Meter(meterName, "1.0.0");
        _ = meter.CreateCounter<long>("requests");

        MetricBudgetOptions options = new MetricBudgetOptions()
            .ForInstrument(meterName, "unknown.instrument", budget => budget.MaxObservedSeries = 1);

        using MetricBudgetSession session = MetricBudgetSession.Start(options);
        return session.Complete().ToDiagnosticString();
    }

    private static string CreateNoMeasurementsObservedDiagnostic()
    {
        const string meterName = "tests.metricbudget.documentation-scope.no-measurements-observed";
        using Meter meter = new Meter(meterName, "1.0.0");
        _ = meter.CreateCounter<long>("requests");

        MetricBudgetOptions options = new MetricBudgetOptions()
            .ForInstrument(meterName, "requests", budget => budget.MaxObservedSeries = 1);

        using MetricBudgetSession session = MetricBudgetSession.Start(options);
        return session.Complete().ToDiagnosticString();
    }

    private static string CreateObservationIncompleteDiagnostic()
    {
        const string meterName = "tests.metricbudget.documentation-scope.observation-incomplete";
        using Meter meter = new Meter(meterName, "1.0.0");
        Counter<long> counter = meter.CreateCounter<long>("requests");

        MetricBudgetOptions options = new MetricBudgetOptions
        {
            MaxTrackedSeries = 1,
        };
        options.ForInstrument(meterName, "requests", budget => budget.MaxObservedSeries = 100);

        using MetricBudgetSession session = MetricBudgetSession.Start(options);
        counter.Add(1, new KeyValuePair<string, object?>("tenant", 1));
        counter.Add(1, new KeyValuePair<string, object?>("tenant", 2));

        return session.Complete().ToDiagnosticString();
    }

    private static string[] TrackedFiles(params string[] pathspecs)
    {
        ProcessStartInfo startInfo = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = RepositoryRoot(),
            Arguments = "ls-files -z -- " + string.Join(" ", pathspecs),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start git to enumerate tracked documentation surfaces.");
        string output = process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        Assert.True(
            process.ExitCode == 0,
            "git ls-files failed while enumerating tracked documentation surfaces: " + error);

        return output
            .Split(NulSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(path => path.Replace('\\', '/'))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
    }

    private static bool IsUtf8TextSurfaceFile(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        if (bytes.Any(value => value == 0))
        {
            return false;
        }

        try
        {
            _ = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(bytes);
            return true;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }

    private static string RelativePath(string root, string path)
    {
        return path.Substring(root.Length)
            .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Replace(Path.DirectorySeparatorChar, '/')
            .Replace(Path.AltDirectorySeparatorChar, '/');
    }

    private static string NormalizeText(string value)
    {
        string lineFeedText = value.Replace("\r\n", "\n").Replace('\r', '\n');
        return string.Join("\n", lineFeedText.Split('\n').Select(line => line.TrimEnd()));
    }

    private static string RepositoryRoot()
    {
        DirectoryInfo? directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "KeelMatrix.MetricBudget.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            "Could not find the repository root above '" + AppContext.BaseDirectory + "'.");
    }

    private enum SurfaceKind
    {
        Markdown,
        TrackedText,
        Xml,
        RuntimeDiagnostic,
    }

    private sealed class RuntimeDiagnosticScenario
    {
        internal RuntimeDiagnosticScenario(string identifier, string snapshotRelativePath)
        {
            Identifier = identifier;
            SnapshotRelativePath = snapshotRelativePath;
        }

        internal string Identifier { get; }

        internal string SnapshotRelativePath { get; }
    }

    private sealed class Surface
    {
        internal Surface(string identifier, string? path, SurfaceKind kind, string snapshotRelativePath)
        {
            Identifier = identifier;
            Path = path;
            Kind = kind;
            SnapshotRelativePath = snapshotRelativePath;
        }

        internal string Identifier { get; }

        internal string? Path { get; }

        internal SurfaceKind Kind { get; }

        internal string SnapshotRelativePath { get; }
    }
}
