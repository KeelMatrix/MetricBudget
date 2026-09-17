// Copyright (c) KeelMatrix

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
    private const string RuntimeDiagnosticSurface = "MetricBudgetReport.ToDiagnosticString()";
    private const string RuntimeDiagnosticSnapshot = "surface-runtime-diagnostic.snapshot";
    private const string SnapshotDirectory = "tests/KeelMatrix.MetricBudget.Tests/ApprovedShippedText";
    private const string ApprovalEnvironmentVariable =
        "KEELMATRIX_METRICBUDGET_APPROVE_DOCUMENTATION_SNAPSHOTS";

    private const string ApprovalCommand =
        "$env:KEELMATRIX_METRICBUDGET_APPROVE_DOCUMENTATION_SNAPSHOTS='1'; "
        + "dotnet test tests/KeelMatrix.MetricBudget.Tests/KeelMatrix.MetricBudget.Tests.csproj "
        + "-c Release --no-build --filter FullyQualifiedName~DocumentationScopeTests";

    // This is the one declaration of the documentation surfaces. Exact paths are existence-locked. Wildcards are
    // expanded so a new matching document is included without changing this test.
    private static readonly SurfaceDeclaration[] DocumentationSurfaceDeclarations =
    {
        new SurfaceDeclaration("README.md", SurfaceKind.Markdown),
        new SurfaceDeclaration("PRIVACY.md", SurfaceKind.Markdown),
        new SurfaceDeclaration("src/KeelMatrix.MetricBudget/README.md", SurfaceKind.Markdown),
        new SurfaceDeclaration("docs/*.md", SurfaceKind.Markdown),
        new SurfaceDeclaration("samples/**", SurfaceKind.RecursiveText),
        new SurfaceDeclaration("tests/KeelMatrix.MetricBudget.PackageConsumer/**", SurfaceKind.RecursiveText),
        new SurfaceDeclaration("src/KeelMatrix.MetricBudget/bin/Release/net8.0/KeelMatrix.MetricBudget.xml", SurfaceKind.Xml),
        new SurfaceDeclaration("src/KeelMatrix.MetricBudget/bin/Release/netstandard2.0/KeelMatrix.MetricBudget.xml", SurfaceKind.Xml),
        new SurfaceDeclaration(RuntimeDiagnosticSurface, SurfaceKind.RuntimeDiagnostic),
    };

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
        string diagnostic = CreateCanonicalDiagnostic();

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

        foreach (SurfaceDeclaration declaration in DocumentationSurfaceDeclarations)
        {
            if (declaration.Kind == SurfaceKind.RuntimeDiagnostic)
            {
                surfaces.Add(new Surface(RuntimeDiagnosticSurface, null, declaration.Kind, RuntimeDiagnosticSnapshot));
                continue;
            }

            string normalized = declaration.Pattern.Replace('/', Path.DirectorySeparatorChar);
            if (normalized.IndexOf('*') < 0)
            {
                string exactPath = Path.Combine(repositoryRoot, normalized);
                Assert.True(File.Exists(exactPath), "Declared documentation surface is missing: " + declaration.Pattern);
                surfaces.Add(new Surface(
                    declaration.Pattern,
                    exactPath,
                    declaration.Kind,
                    SnapshotRelativePath(declaration.Pattern)));
                continue;
            }

            int wildcardIndex = normalized.IndexOf('*');
            string baseDirectory = normalized.Substring(0, wildcardIndex).TrimEnd(Path.DirectorySeparatorChar);
            string directoryPath = Path.Combine(repositoryRoot, baseDirectory);
            Assert.True(
                Directory.Exists(directoryPath),
                "Declared documentation directory is missing: " + declaration.Pattern);

            bool recursive = declaration.Pattern.EndsWith("/**", StringComparison.Ordinal);
            SearchOption searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            string searchPattern = recursive
                ? "*"
                : normalized.Substring(normalized.LastIndexOf(Path.DirectorySeparatorChar) + 1);
            string[] matches = Directory.GetFiles(directoryPath, searchPattern, searchOption)
                .Where(path => IsTextSurfaceFile(repositoryRoot, path))
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            Assert.True(matches.Length > 0, "Declared documentation glob matched no files: " + declaration.Pattern);
            foreach (string match in matches)
            {
                string relative = RelativePath(repositoryRoot, match);
                surfaces.Add(new Surface(relative, match, declaration.Kind, SnapshotRelativePath(relative)));
            }
        }

        return surfaces;
    }

    private static string SurfaceText(Surface surface)
    {
        return surface.Kind == SurfaceKind.RuntimeDiagnostic
            ? CreateCanonicalDiagnostic()
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

    private static string CreateCanonicalDiagnostic()
    {
        const string meterName = "tests.metricbudget.documentation-scope";
        const string instrumentName = "requests";

        using Meter meter = new Meter(meterName, "1.0.0");
        Counter<long> counter = meter.CreateCounter<long>(instrumentName);

        MetricBudgetOptions options = new MetricBudgetOptions
        {
            MaxTrackedSeries = 1,
        };
        options.ForInstrument(meterName, instrumentName, budget => budget.MaxObservedSeries = 100);

        using MetricBudgetSession session = MetricBudgetSession.Start(options);
        counter.Add(1, new KeyValuePair<string, object?>("tenant", 1));
        counter.Add(1, new KeyValuePair<string, object?>("tenant", 2));

        return session.Complete().ToDiagnosticString();
    }

    private static bool IsTextSurfaceFile(string repositoryRoot, string path)
    {
        string relativePath = RelativePath(repositoryRoot, path);
        string[] segments = relativePath.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar });
        return !segments.Any(segment => string.Equals(segment, "bin", StringComparison.OrdinalIgnoreCase)
            || string.Equals(segment, "obj", StringComparison.OrdinalIgnoreCase));
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
        RecursiveText,
        Xml,
        RuntimeDiagnostic,
    }

    private sealed class SurfaceDeclaration
    {
        internal SurfaceDeclaration(string pattern, SurfaceKind kind)
        {
            Pattern = pattern;
            Kind = kind;
        }

        internal string Pattern { get; }

        internal SurfaceKind Kind { get; }
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
