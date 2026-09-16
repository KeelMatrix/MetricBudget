// Copyright (c) KeelMatrix

using System.Diagnostics.Metrics;
using System.IO;
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

    private static readonly Regex[] SessionScopedBoundWording =
    {
        new Regex(@"\bapplies\s+across\s+the\s+session\b", RegexOptions.IgnoreCase),
        new Regex(@"\bapplies\s+across\s+all\s+selected\s+instruments\b", RegexOptions.IgnoreCase),
        new Regex(@"\bapplies\s+once\s+per\s+session\b", RegexOptions.IgnoreCase),
        new Regex(@"\b(?:series|tag[- ]value)\s+(?:safety\s+)?bound\s+(?:is|applies\s+(?:to|across))\s+(?:the\s+)?session[- ]wide\b", RegexOptions.IgnoreCase),
        new Regex(@"\b(?:series|tag[- ]value)\s+(?:safety\s+)?bound\s+(?:is|applies\s+(?:to|across))\s+(?:the\s+)?session\b", RegexOptions.IgnoreCase),
        new Regex(@"\b(?:series|tag[- ]value)\s+(?:safety\s+)?bound\s+is\s+(?:one\s+)?shared\s+pool\s+for\s+the\s+session\b", RegexOptions.IgnoreCase),
        new Regex(@"\bthe\s+session\s+shares\s+one\s+(?:series|tag[- ]value)\s+bound\b", RegexOptions.IgnoreCase),
        new Regex(@"\b(?:one\s+)?(?:series|tag[- ]value)\s+bound\s+across\s+(?:the\s+)?session\b", RegexOptions.IgnoreCase),
        new Regex(@"\b(?:MaxTrackedSeries|MaxTrackedValuesPerTag)\s+(?:is|applies\s+(?:to|across)|covers?)\s+(?:the\s+)?session(?:[- ]wide)?\b", RegexOptions.IgnoreCase),
        new Regex(@"\b(?:the\s+)?session[- ]wide\s+(?:series|tag[- ]value|distinct[- ]value)\s+(?:safety\s+)?bound\b", RegexOptions.IgnoreCase),
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
            AssertNoSessionScopedBoundWording(Normalize(documentation.ToString()), documentationPath);

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
            AssertNoSessionScopedBoundWording(File.ReadAllText(surfaceFile), surfaceFile);
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

        AssertNoSessionScopedBoundWording(diagnostic, "MetricBudgetReport.ToDiagnosticString()");
        Assert.Contains(
            "because the instrument's series safety bound was reached",
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

    private static void AssertNoSessionScopedBoundWording(string text, string surfacePath)
    {
        foreach (Regex wording in SessionScopedBoundWording)
        {
            Match match = wording.Match(text);
            Assert.False(
                match.Success,
                "False session-scoped series or tag-value bound wording was found in " + surfacePath
                    + ": '" + (match.Success ? match.Value : wording.ToString()) + "'.");
        }
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
