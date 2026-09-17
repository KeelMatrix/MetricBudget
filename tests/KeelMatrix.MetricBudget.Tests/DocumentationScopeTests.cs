// Copyright (c) KeelMatrix

using System.Diagnostics.Metrics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace KeelMatrix.MetricBudget.Tests;

/// <summary>
/// Guards the shipped XML documentation and declared text surfaces with a closed, exact sentence inventory.
/// </summary>
public sealed class DocumentationScopeTests
{
    private const string RuntimeDiagnosticSurface = "MetricBudgetReport.ToDiagnosticString()";
    private const string XmlSurface = "src/KeelMatrix.MetricBudget/bin/Release/{tfm}/KeelMatrix.MetricBudget.xml";

    // This is the one declaration of the documentation surfaces. Exact paths are existence-locked. Wildcards are
    // expanded so a new matching document is scanned without changing this test.
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

    // These are deliberate floors, not a count inferred from the inventory. Removing a shipped bound statement
    // and its inventory entry therefore fails until the maintainer explicitly changes this declaration as well.
    private static readonly SurfaceFloor[] DocumentationSurfaceFloors =
    {
        new SurfaceFloor("README.md", 1),
        new SurfaceFloor("PRIVACY.md", 0),
        new SurfaceFloor("src/KeelMatrix.MetricBudget/README.md", 14),
        new SurfaceFloor("docs/DEV.md", 2),
        new SurfaceFloor("docs/observed-vs-production-cardinality.md", 2),
        new SurfaceFloor("docs/phase0-probe-evidence.md", 1),
        new SurfaceFloor("docs/privacy-and-telemetry.md", 1),
        new SurfaceFloor("docs/safety-bounds.md", 12),
        new SurfaceFloor("docs/series-identity.md", 0),
        new SurfaceFloor("docs/testing-internals.md", 0),
        new SurfaceFloor("docs/troubleshooting.md", 6),
        new SurfaceFloor("samples/KeelMatrix.MetricBudget.Sample/KeelMatrix.MetricBudget.Sample.csproj", 0),
        new SurfaceFloor("samples/KeelMatrix.MetricBudget.Sample/NuGet.config", 0),
        new SurfaceFloor("samples/KeelMatrix.MetricBudget.Sample/Program.cs", 0),
        new SurfaceFloor("tests/KeelMatrix.MetricBudget.PackageConsumer/KeelMatrix.MetricBudget.PackageConsumer.csproj", 0),
        new SurfaceFloor("tests/KeelMatrix.MetricBudget.PackageConsumer/NuGet.config", 0),
        new SurfaceFloor("tests/KeelMatrix.MetricBudget.PackageConsumer/Program.cs", 0),
        new SurfaceFloor(XmlSurface, 49),
        new SurfaceFloor(RuntimeDiagnosticSurface, 3),
    };

    private static readonly string[] DocumentationTargetFrameworks =
    {
        "net8.0",
        "netstandard2.0",
    };

    private static readonly char[] DiagnosticLineBreaks = { '\r', '\n' };

    // These markers only identify units that require the closed inventory. They never approve a unit. Approval is
    // possible only through an exact normalized inventory entry below; no verb, adjective, noun, or qualifier is
    // used to decide whether a sentence is semantically correct.
    private static readonly string[] BoundUnitMarkers =
    {
        "bound",
        "observed-series",
        "series budget",
        "tag budget",
        "tag-value budget",
        "distinct-value budget",
        "MaxTrackedSeries",
        "MaxTrackedValuesPerTag",
        "MaxObservedSeries",
        "MaxDistinctValues",
    };

    // The inventory is intentionally explicit and finite. Each entry is a normalized unit as it appears on one
    // declared surface. The separately declared floor requires every approved identity to remain present even when
    // a changed text no longer contains a marker.
    private static readonly ApprovedUnit[] ApprovedBoundUnits = LoadApprovedBoundUnits();

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
    public void SeriesBoundXmlDocumentationStatesThePerInstrumentScopeOnly()
    {
        foreach (string targetFramework in DocumentationTargetFrameworks)
        {
            string documentationPath = Path.Combine(
                RepositoryRoot(),
                "src", "KeelMatrix.MetricBudget", "bin", "Release", targetFramework,
                "KeelMatrix.MetricBudget.xml");

            Assert.True(
                File.Exists(documentationPath),
                "The library's XML documentation was not found at " + documentationPath
                    + ". Build the library in Release for both target frameworks before running this test.");

            Surface surface = new Surface(XmlSurface, documentationPath, SurfaceKind.Xml);
            AssertSurfaceInventory(surface);

            XDocument documentation = XDocument.Load(documentationPath);
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
    public void CanonicalTextSurfacesUseOnlyApprovedBoundUnits()
    {
        IReadOnlyList<Surface> surfaces = DocumentationSurfaceFiles();
        AssertInventorySurfacesAreDeclared(surfaces);

        foreach (Surface surface in surfaces)
        {
            AssertSurfaceInventory(surface);
        }

        AssertFloorDeclarations(surfaces);
    }

    [Fact]
    public void DiagnosticSeriesBoundNamesTheInstrumentScope()
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

        string diagnostic = session.Complete().ToDiagnosticString();

        AssertBoundScopeInvariant(diagnostic, RuntimeDiagnosticSurface);
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

        string summary = Normalize(member.Element("summary")?.Value ?? string.Empty);
        Assert.Contains("instrument identity", summary, StringComparison.OrdinalIgnoreCase);
    }

    private static void AssertSurfaceInventory(Surface surface)
    {
        List<ApprovedUnit> expected = ApprovedBoundUnits
            .Where(entry => entry.Matches(surface.InventorySurface))
            .ToList();
        HashSet<string> approved = new HashSet<string>(
            expected.Select(entry => entry.Text),
            StringComparer.Ordinal);
        List<string> unknown = new List<string>();
        HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
        int approvedCount = 0;

        foreach (string unit in TextUnits(surface))
        {
            string normalized = Normalize(unit);
            if (!RequiresClosedApproval(normalized))
            {
                continue;
            }

            if (!approved.Contains(normalized))
            {
                unknown.Add(normalized);
            }
            else
            {
                approvedCount++;
                seen.Add(normalized);
            }
        }

        Assert.True(
            unknown.Count == 0,
            "Closed bound-sentence inventory rejected " + surface.InventorySurface + ". Offending unit(s):\n"
                + string.Join("\n", unknown.Select(unit => "- " + unit))
                + "\nApprove a new or reworded unit deliberately by adding its normalized text to the finite inventory "
                + "for this surface and updating its per-surface floor count.");

        Assert.True(
            approvedCount >= FloorFor(surface.InventorySurface),
            "The approved-unit floor for " + surface.InventorySurface + " is " + FloorFor(surface.InventorySurface)
                + ", but only " + approvedCount + " approved bound-mentioning unit occurrence(s) remain.");

        string[] missing = expected
            .Where(entry => !seen.Contains(entry.Text))
            .Select(entry => entry.Text)
            .ToArray();
        Assert.True(
            missing.Length == 0,
            "The approved-unit floor for " + surface.InventorySurface
                + " is missing these exact inventory entries:\n- " + string.Join("\n- ", missing));
    }

    private static void AssertBoundScopeInvariant(string text, string surfacePath)
    {
        Surface surface = new Surface(surfacePath, string.Empty, SurfaceKind.RuntimeDiagnostic);
        List<ApprovedUnit> expected = ApprovedBoundUnits
            .Where(entry => entry.Matches(surface.InventorySurface))
            .ToList();
        HashSet<string> approved = new HashSet<string>(expected.Select(entry => entry.Text), StringComparer.Ordinal);
        List<string> unknown = new List<string>();
        HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
        int approvedCount = 0;

        foreach (string line in text.Split(DiagnosticLineBreaks, StringSplitOptions.RemoveEmptyEntries))
        {
            string normalized = Normalize(line);
            if (!RequiresClosedApproval(normalized))
            {
                continue;
            }

            if (!approved.Contains(normalized))
            {
                unknown.Add(normalized);
            }
            else
            {
                approvedCount++;
                seen.Add(normalized);
            }
        }

        Assert.True(
            unknown.Count == 0,
            "Closed bound-sentence inventory rejected " + surfacePath + ". Offending unit(s):\n"
                + string.Join("\n", unknown.Select(unit => "- " + unit))
                + "\nApprove a new or reworded unit deliberately by adding its normalized text to the finite inventory "
                + "for this surface and updating its per-surface floor count.");
        Assert.Equal(FloorFor(surfacePath), approvedCount);
        string[] missing = expected
            .Where(entry => !seen.Contains(entry.Text))
            .Select(entry => entry.Text)
            .ToArray();
        Assert.True(
            missing.Length == 0,
            "The approved-unit floor for " + surfacePath
                + " is missing these exact inventory entries:\n- " + string.Join("\n- ", missing));
    }

#pragma warning disable CA2249
    private static bool RequiresClosedApproval(string text)
    {
        foreach (string marker in BoundUnitMarkers)
        {
            if (marker.Length > 0 && char.IsLetter(marker[0]) && char.IsLower(marker[0])
                ? ContainsWord(text, marker)
                : text.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsWord(string text, string word)
    {
        int offset = 0;
        while (offset < text.Length)
        {
            int index = text.IndexOf(word, offset, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
            {
                return false;
            }

            bool startsWord = index == 0 || !IsWordCharacter(text[index - 1]);
            int end = index + word.Length;
            bool endsWord = end == text.Length || !IsWordCharacter(text[end]);
            if (startsWord && endsWord)
            {
                return true;
            }

            offset = end;
        }

        return false;
    }
    private static bool IsWordCharacter(char value)
    {
        return char.IsLetterOrDigit(value) || value == '_';
    }
#pragma warning restore CA2249

    private static IReadOnlyList<string> TextUnits(Surface surface)
    {
        if (surface.Kind == SurfaceKind.RuntimeDiagnostic)
        {
            return Array.Empty<string>();
        }

        if (surface.Kind == SurfaceKind.Xml)
        {
            return XmlUnits(surface.Path);
        }

        string extension = Path.GetExtension(surface.Path);
        if (string.Equals(extension, ".md", StringComparison.OrdinalIgnoreCase))
        {
            return MarkdownUnits(File.ReadAllLines(surface.Path));
        }

        if (string.Equals(extension, ".cs", StringComparison.OrdinalIgnoreCase))
        {
            return CSharpTextUnits(File.ReadAllText(surface.Path));
        }

        return SentenceUnits(File.ReadAllText(surface.Path));
    }

    private static List<string> XmlUnits(string path)
    {
        XDocument documentation = XDocument.Load(path);
        List<string> units = new List<string>();
        foreach (XElement member in documentation.Descendants("member"))
        {
            units.AddRange(SentenceUnits((string?)member.Attribute("name") + " " + member.Value));
        }

        return units;
    }

    private static List<string> MarkdownUnits(string[] lines)
    {
        List<string> units = new List<string>();
        StringBuilder paragraph = new StringBuilder();
        StringBuilder listItem = new StringBuilder();
        bool inCodeFence = false;
        bool inListItem = false;

        void FlushParagraph()
        {
            if (paragraph.Length == 0)
            {
                return;
            }

            units.AddRange(SentenceUnits(paragraph.ToString()));
            paragraph.Clear();
        }

        void FlushListItem()
        {
            if (listItem.Length == 0)
            {
                return;
            }

            string normalized = Normalize(listItem.ToString());
            if (normalized.Length > 0)
            {
                units.Add(normalized);
            }

            listItem.Clear();
        }

        foreach (string line in lines)
        {
            string trimmed = line.Trim();
            if (trimmed.StartsWith("```", StringComparison.Ordinal))
            {
                FlushListItem();
                inListItem = false;
                FlushParagraph();
                inCodeFence = !inCodeFence;
                continue;
            }

            if (inCodeFence)
            {
                continue;
            }

            if (trimmed.Length > 0 && trimmed[0] == '|' && trimmed[trimmed.Length - 1] == '|')
            {
                FlushListItem();
                inListItem = false;
                FlushParagraph();
                string row = trimmed.Substring(1, trimmed.Length - 2);
                foreach (string cell in row.Split('|'))
                {
                    string normalized = Normalize(cell);
                    if (normalized.Length > 0)
                    {
                        units.Add(normalized);
                    }
                }

                continue;
            }

            if (Regex.IsMatch(trimmed, "^#{1,6}\\s+"))
            {
                FlushListItem();
                inListItem = false;
                FlushParagraph();
                units.Add(Normalize(Regex.Replace(trimmed, "^#{1,6}\\s+", string.Empty)));
                continue;
            }

            if (Regex.IsMatch(trimmed, "^([-*+]|\\d+[.)])\\s+"))
            {
                FlushParagraph();
                FlushListItem();
                inListItem = true;
                listItem.Append(Regex.Replace(trimmed, "^([-*+]|\\d+[.)])\\s+", string.Empty));
                continue;
            }

            if (string.IsNullOrWhiteSpace(trimmed))
            {
                FlushListItem();
                inListItem = false;
                FlushParagraph();
                continue;
            }

            StringBuilder target = inListItem ? listItem : paragraph;
            if (target.Length > 0)
            {
                target.Append(' ');
            }

            target.Append(trimmed);
        }

        FlushListItem();
        FlushParagraph();
        return units;
    }

    private static List<string> CSharpTextUnits(string text)
    {
        List<string> units = new List<string>();
        Regex token = new Regex(
            "//(?<comment>[^\\r\\n]*)|@\"(?<verbatim>(?:\"\"|[^\"])*)\"|\"(?<string>(?:\\\\.|[^\"\\\\])*)\"",
            RegexOptions.Multiline);

        foreach (Match match in token.Matches(text))
        {
            string value = match.Groups["comment"].Success
                ? match.Groups["comment"].Value
                : match.Groups["verbatim"].Success
                    ? match.Groups["verbatim"].Value.Replace("\"\"", "\"")
                    : match.Groups["string"].Value;
            units.AddRange(SentenceUnits(value));
        }

        return units;
    }

    private static string[] SentenceUnits(string text)
    {
        string normalized = Normalize(text);
        if (normalized.Length == 0)
        {
            return Array.Empty<string>();
        }

        return Regex.Split(normalized, "(?<=[.!?])\\s+")
            .Where(unit => unit.Length > 0)
            .ToArray();
    }

    private static List<Surface> DocumentationSurfaceFiles()
    {
        string repositoryRoot = RepositoryRoot();
        List<Surface> surfaces = new List<Surface>();

        foreach (SurfaceDeclaration declaration in DocumentationSurfaceDeclarations)
        {
            if (declaration.Kind == SurfaceKind.RuntimeDiagnostic)
            {
                continue;
            }

            string normalized = declaration.Pattern.Replace('/', Path.DirectorySeparatorChar);
            if (normalized.IndexOf('*') < 0)
            {
                string exactPath = Path.Combine(repositoryRoot, normalized);
                Assert.True(File.Exists(exactPath), "Declared documentation surface is missing: " + declaration.Pattern);
                surfaces.Add(new Surface(InventorySurface(declaration.Pattern), exactPath, declaration.Kind));
                continue;
            }

            int wildcardIndex = normalized.IndexOf('*');
            string baseDirectory = normalized.Substring(0, wildcardIndex).TrimEnd(Path.DirectorySeparatorChar);
            string directoryPath = Path.Combine(repositoryRoot, baseDirectory);
            Assert.True(Directory.Exists(directoryPath), "Declared documentation directory is missing: " + declaration.Pattern);

            bool recursive = declaration.Pattern.EndsWith("/**", StringComparison.Ordinal);
            SearchOption searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            string searchPattern = recursive ? "*" : normalized.Substring(normalized.LastIndexOf(Path.DirectorySeparatorChar) + 1);
            string[] matches = Directory.GetFiles(directoryPath, searchPattern, searchOption)
                .Where(path => IsTextSurfaceFile(repositoryRoot, path))
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            Assert.True(matches.Length > 0, "Declared documentation glob matched no files: " + declaration.Pattern);
            foreach (string match in matches)
            {
                string relative = match.Substring(repositoryRoot.Length + 1)
                    .Replace(Path.DirectorySeparatorChar, '/');
                surfaces.Add(new Surface(relative, match, declaration.Kind));
            }
        }

        return surfaces;
    }

    private static string InventorySurface(string declaration)
    {
        return declaration.StartsWith("src/KeelMatrix.MetricBudget/bin/Release/", StringComparison.Ordinal)
            ? XmlSurface
            : declaration;
    }

    private static void AssertInventorySurfacesAreDeclared(IReadOnlyList<Surface> surfaces)
    {
        HashSet<string> declared = new HashSet<string>(
            surfaces.Select(surface => surface.InventorySurface),
            StringComparer.Ordinal);
        declared.Add(RuntimeDiagnosticSurface);
        string[] unknown = ApprovedBoundUnits
            .Select(entry => entry.Surface)
            .Distinct(StringComparer.Ordinal)
            .Where(surface => !declared.Contains(surface))
            .OrderBy(surface => surface, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            unknown.Length == 0,
            "The closed documentation inventory contains undeclared surface(s): "
                + string.Join(", ", unknown));
    }

    private static void AssertFloorDeclarations(IReadOnlyList<Surface> surfaces)
    {
        string[] declared = surfaces
            .Select(surface => surface.InventorySurface)
            .Append(RuntimeDiagnosticSurface)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(surface => surface, StringComparer.Ordinal)
            .ToArray();
        string[] floorSurfaces = DocumentationSurfaceFloors
            .Select(floor => floor.Surface)
            .OrderBy(surface => surface, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(floorSurfaces, declared);
        foreach (string surface in declared)
        {
            int inventoryCount = ApprovedBoundUnits.Count(entry => entry.Matches(surface));
            Assert.Equal(FloorFor(surface), inventoryCount);
        }
    }

    private static int FloorFor(string surface)
    {
        return DocumentationSurfaceFloors
            .Single(floor => string.Equals(floor.Surface, surface, StringComparison.Ordinal))
            .Count;
    }

    private static bool IsTextSurfaceFile(string repositoryRoot, string path)
    {
        string relativePath = path.Substring(repositoryRoot.Length + 1);
        string[] segments = relativePath.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar });
        return !segments.Any(segment => string.Equals(segment, "bin", StringComparison.OrdinalIgnoreCase)
            || string.Equals(segment, "obj", StringComparison.OrdinalIgnoreCase));
    }

    private static string Normalize(string value)
    {
        return string.Join(" ", value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
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

    private static ApprovedUnit[] LoadApprovedBoundUnits()
    {
        string path = Path.Combine(
            RepositoryRoot(),
            "tests",
            "KeelMatrix.MetricBudget.Tests",
            "DocumentationScopeInventory.txt");
        if (!File.Exists(path))
        {
            throw new InvalidOperationException("The closed documentation inventory is missing: " + path);
        }

        List<ApprovedUnit> entries = new List<ApprovedUnit>();
        HashSet<string> identities = new HashSet<string>(StringComparer.Ordinal);
        foreach (string line in File.ReadAllLines(path))
        {
            if (string.IsNullOrWhiteSpace(line) || (line.Length > 0 && line[0] == '#'))
            {
                continue;
            }

            int separator = line.IndexOf('\t');
            if (separator <= 0 || separator == line.Length - 1)
            {
                throw new InvalidOperationException(
                    "The closed documentation inventory must use '<surface><TAB><normalized unit>': " + line);
            }

            ApprovedUnit entry = new ApprovedUnit(line.Substring(0, separator), line.Substring(separator + 1));
            string identity = entry.Surface + "\n" + entry.Text;
            if (!identities.Add(identity))
            {
                throw new InvalidOperationException("The closed documentation inventory contains a duplicate: " + line);
            }

            entries.Add(entry);
        }

        return entries.ToArray();
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

    private sealed class SurfaceFloor
    {
        internal SurfaceFloor(string surface, int count)
        {
            Surface = surface;
            Count = count;
        }

        internal string Surface { get; }

        internal int Count { get; }
    }

    private sealed class Surface
    {
        internal Surface(string inventorySurface, string path, SurfaceKind kind)
        {
            InventorySurface = inventorySurface;
            Path = path;
            Kind = kind;
        }

        internal string InventorySurface { get; }

        internal string Path { get; }

        internal SurfaceKind Kind { get; }
    }

    private sealed class ApprovedUnit
    {
        internal ApprovedUnit(string surface, string text)
        {
            Surface = surface;
            Text = Normalize(text);
        }

        internal string Surface { get; }

        internal string Text { get; }

        internal bool Matches(string surface)
        {
            return string.Equals(Surface, surface, StringComparison.Ordinal);
        }
    }
}
