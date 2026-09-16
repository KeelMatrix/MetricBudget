// Copyright (c) KeelMatrix

using System.Diagnostics.Metrics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace KeelMatrix.MetricBudget.Tests;

/// <summary>
/// Guards the shipped XML documentation of the series safety bound. These comments compile into the package's
/// XML documentation file, so a wrong sentence here is a wrong sentence inside the published package.
/// </summary>
public sealed class DocumentationScopeTests
{
    // Keep this list as the single declaration of the canonical shipped text surfaces. Exact paths are required
    // to exist; wildcard paths are expanded so new matching files are covered automatically.
    private static readonly string[] DocumentationSurfaceGlobs =
    {
        "README.md",
        "PRIVACY.md",
        "src/KeelMatrix.MetricBudget/README.md",
        "docs/*.md",
        "samples/**",
        "tests/KeelMatrix.MetricBudget.PackageConsumer/**",
    };

    private static readonly string[] DocumentationTargetFrameworks =
    {
        "net8.0",
        "netstandard2.0",
    };

    // This is deliberately a positive invariant. It recognizes the bounded concepts, then requires the same
    // sentence or table cell to identify the instrument scope. The wording is not a blacklist: an unseen
    // session-scope synonym is rejected because it lacks the required identity phrase.
    private static readonly Regex BoundMention = new Regex(
        @"\b(?:series\s+(?:safety\s+)?(?:bound|cap|limit|ceiling|pool)|"
            + @"(?:observed[- ]series|series)\s+budget|"
            + @"(?:tag[- ]value|distinct[- ]value)\s+budget|"
            + @"tag[- ]value\s+(?:safety\s+)?(?:bound|cap|limit|pool)|"
            + @"(?:distinct[- ]value|tag\s+value)\s+(?:safety\s+)?(?:bound|cap|limit|pool)|"
            + @"(?:MaxTrackedSeries|MaxTrackedValuesPerTag|MaxObservedSeries|MaxDistinctValues))\b",
        RegexOptions.IgnoreCase);

    private static readonly Regex InstrumentIdentityScope = new Regex(
        @"\b(?:instrument\s+identit(?:y|ies)|instrument's\s+identity|per[- ](?:matched[- ]|matched\s+)?instrument[- ]identit(?:y|ies)|"
            + @"for\s+(?:one|each|every|the|that|this|matched)\s+instrument\s+identit(?:y|ies)|"
            + @"on\s+(?:one|each|every|the|that|this|matched)\s+instrument\s+identit(?:y|ies)|"
            + @"number\s+of\s+matched\s+instrument\s+identities)\b",
        RegexOptions.IgnoreCase);

    private static readonly Regex RuntimeSafetyBoundsMention = new Regex(
        @"\bsafety\s+bounds\b",
        RegexOptions.IgnoreCase);

    private static readonly char[] DiagnosticLineBreaks = { '\r', '\n' };

    private static readonly CorrectiveStatement[] DeclaredCorrectiveStatements =
    {
        new CorrectiveStatement(
            "src/KeelMatrix.MetricBudget/MetricBudgetOptions.cs",
            "The session does not share one series bound across selected instruments.",
            "This single corrective sentence rejects the session-pool interpretation while the surrounding API documentation states the positive per-instrument invariant."),
        new CorrectiveStatement(
            "tests/KeelMatrix.MetricBudget.Tests/PerInstrumentBoundScopeTests.cs",
            "is exactly why the documentation must not describe the bound as one session-wide pool.",
            "This single test-comment sentence explains why the regression test exists; it is not a product claim."),
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

            XDocument documentation = XDocument.Load(documentationPath);
            AssertBoundScopeInvariant(documentation, documentationPath);

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
    public void CanonicalTextSurfacesDoNotUseSessionScopedBoundWording()
    {
        IReadOnlyList<string> surfaceFiles = DocumentationSurfaceFiles();

        foreach (string surfaceFile in surfaceFiles)
        {
            foreach (string unit in TextUnits(surfaceFile))
            {
                AssertBoundScopeInvariant(unit, surfaceFile);
            }
        }
    }

    [Fact]
    public void DiagnosticSeriesBoundNamesTheInstrumentScope()
    {
        string meterName = TestNames.Meter(nameof(DiagnosticSeriesBoundNamesTheInstrumentScope));
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

        string diagnostic = session.Complete().ToDiagnosticString();

        AssertBoundScopeInvariant(diagnostic, "MetricBudgetReport.ToDiagnosticString()");
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

        // The summary sentence a caller sees in IntelliSense must name the instrument-identity scope itself.
        Assert.Contains("instrument identity", summary, StringComparison.OrdinalIgnoreCase);
    }

    private static void AssertBoundScopeInvariant(XDocument documentation, string surfacePath)
    {
        foreach (XElement member in documentation.Descendants("member"))
        {
            AssertBoundScopeInvariant(
                Normalize((string?)member.Attribute("name") + " " + member.Value),
                surfacePath);
        }
    }

    private static void AssertBoundScopeInvariant(string text, string surfacePath)
    {
        IEnumerable<string> units = string.Equals(surfacePath, "MetricBudgetReport.ToDiagnosticString()", StringComparison.Ordinal)
            ? text.Split(DiagnosticLineBreaks, StringSplitOptions.RemoveEmptyEntries).SelectMany(SentenceUnits)
            : SentenceUnits(text);

        foreach (string unit in units)
        {
            AssertBoundScopeUnitInvariant(unit, surfacePath);
        }
    }

    private static void AssertBoundScopeUnitInvariant(string text, string surfacePath)
    {
        foreach (Match match in BoundMention.Matches(text))
        {
            if (InstrumentIdentityScope.IsMatch(text) || IsDeclaredCorrective(text))
            {
                continue;
            }

            Assert.Fail(
                "A series or tag-value bound mention must name the instrument-identity scope in " + surfacePath
                    + ": '" + text + "'.");
        }

        if (string.Equals(surfacePath, "MetricBudgetReport.ToDiagnosticString()", StringComparison.Ordinal)
            && RuntimeSafetyBoundsMention.IsMatch(text)
            && !InstrumentIdentityScope.IsMatch(text))
        {
            Assert.Fail(
                "The diagnostic safety-bound summary must name the instrument-identity scope: '" + text + "'.");
        }
    }

    private static bool IsDeclaredCorrective(string text)
    {
        string normalized = Normalize(text);
        foreach (CorrectiveStatement statement in DeclaredCorrectiveStatements)
        {
            if (string.Equals(normalized, statement.Sentence, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static IReadOnlyList<string> TextUnits(string path)
    {
        string extension = Path.GetExtension(path);
        if (string.Equals(extension, ".md", StringComparison.OrdinalIgnoreCase))
        {
            return MarkdownUnits(File.ReadAllLines(path));
        }

        if (string.Equals(extension, ".cs", StringComparison.OrdinalIgnoreCase))
        {
            return CSharpTextUnits(File.ReadAllText(path));
        }

        return SentenceUnits(File.ReadAllText(path));
    }

    private static List<string> MarkdownUnits(string[] lines)
    {
        List<string> units = new List<string>();
        StringBuilder paragraph = new StringBuilder();
        bool inCodeFence = false;

        void FlushParagraph()
        {
            if (paragraph.Length == 0)
            {
                return;
            }

            units.AddRange(SentenceUnits(paragraph.ToString()));
            paragraph.Clear();
        }

        foreach (string line in lines)
        {
            string trimmed = line.TrimStart();
            if (trimmed.StartsWith("```", StringComparison.Ordinal))
            {
                FlushParagraph();
                inCodeFence = !inCodeFence;
                continue;
            }

            if (inCodeFence)
            {
                continue;
            }

            if (trimmed.Length >= 2 && trimmed[0] == '|' && trimmed[trimmed.Length - 1] == '|')
            {
                FlushParagraph();
                string row = trimmed.Substring(1, trimmed.Length - 2);
                string[] cells = row.Split('|');
                foreach (string cell in cells)
                {
                    units.AddRange(SentenceUnits(cell));
                }

                continue;
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                FlushParagraph();
                continue;
            }

            if (paragraph.Length > 0)
            {
                paragraph.Append(' ');
            }

            paragraph.Append(trimmed);
        }

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

        return Regex.Split(normalized, @"(?<=[.!?])\s+")
            .Where(unit => unit.Length > 0)
            .ToArray();
    }

    private sealed class CorrectiveStatement
    {
        internal CorrectiveStatement(string source, string sentence, string reason)
        {
            Source = source;
            Sentence = sentence;
            Reason = reason;
        }

        internal string Source { get; }

        internal string Sentence { get; }

        internal string Reason { get; }
    }

    private static string[] DocumentationSurfaceFiles()
    {
        string repositoryRoot = RepositoryRoot();
        SortedSet<string> files = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string surfaceGlob in DocumentationSurfaceGlobs)
        {
            string normalizedGlob = surfaceGlob.Replace('/', Path.DirectorySeparatorChar);
            if (normalizedGlob.IndexOf('*') < 0)
            {
                string exactPath = Path.Combine(repositoryRoot, normalizedGlob);
                Assert.True(File.Exists(exactPath), "Declared documentation surface is missing: " + surfaceGlob);
                files.Add(exactPath);
                continue;
            }

            string baseDirectory = normalizedGlob.Substring(0, normalizedGlob.IndexOf('*'));
            baseDirectory = baseDirectory.TrimEnd(Path.DirectorySeparatorChar);
            string pattern = normalizedGlob.Substring(normalizedGlob.LastIndexOf(Path.DirectorySeparatorChar) + 1);
            string directoryPath = Path.Combine(repositoryRoot, baseDirectory);

            Assert.True(Directory.Exists(directoryPath), "Declared documentation directory is missing: " + surfaceGlob);

            SearchOption searchOption = surfaceGlob.EndsWith("/**", StringComparison.Ordinal)
                ? SearchOption.AllDirectories
                : SearchOption.TopDirectoryOnly;
            string searchPattern = searchOption == SearchOption.AllDirectories ? "*" : pattern;
            string[] matches = Directory.GetFiles(directoryPath, searchPattern, searchOption);

            foreach (string match in matches)
            {
                if (searchOption == SearchOption.AllDirectories && !IsTextSurfaceFile(repositoryRoot, match))
                {
                    continue;
                }

                files.Add(match);
            }

            Assert.True(
                files.Any(path => path.StartsWith(directoryPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)),
                "Declared documentation glob matched no files: " + surfaceGlob);
        }

        return files.ToArray();
    }

    private static bool IsTextSurfaceFile(string repositoryRoot, string path)
    {
        string relativePath = path.Substring(repositoryRoot.Length + 1);
        string[] segments = relativePath.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar });
        return !segments.Any(segment => string.Equals(segment, "bin", StringComparison.OrdinalIgnoreCase)
            || string.Equals(segment, "obj", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Collapses the line breaks and indentation the documentation file adds so assertions read the sentences a
    /// user of the package actually sees.
    /// </summary>
    private static string Normalize(string value)
    {
        return string.Join(" ", value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>
    /// Walks up from the test host's base directory to the repository root, so the guard reads the same source tree
    /// the build produced the library from. The base directory is used instead of the assembly location because the
    /// .NET Framework host shadow-copies the test assembly before running it.
    /// </summary>
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
}
