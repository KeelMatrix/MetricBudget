// Copyright (c) KeelMatrix

using System.Diagnostics.Metrics;
using System.IO;
using System.Xml.Linq;

namespace KeelMatrix.MetricBudget.Tests;

/// <summary>
/// Guards the shipped XML documentation of the series safety bound. These comments compile into the package's
/// XML documentation file, so a wrong sentence here is a wrong sentence inside the published package.
/// </summary>
public sealed class DocumentationScopeTests
{
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
        string documentationPath = Path.Combine(
            RepositoryRoot(),
            "src", "KeelMatrix.MetricBudget", "bin", "Release", "net8.0",
            "KeelMatrix.MetricBudget.xml");

        Assert.True(
            File.Exists(documentationPath),
            "The library's XML documentation was not found at " + documentationPath
                + ". Build the library in Release before running this test.");

        XDocument documentation = XDocument.Load(documentationPath);
        string text = Normalize(documentation.ToString());

        string[] sessionWideWording =
        {
            "across all selected instruments",
            "across the session",
        };

        foreach (string wording in sessionWideWording)
        {
            Assert.DoesNotContain(wording, text, StringComparison.OrdinalIgnoreCase);
        }

        Assert.Contains("per instrument identity", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("matched instrument identities", text, StringComparison.OrdinalIgnoreCase);
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
