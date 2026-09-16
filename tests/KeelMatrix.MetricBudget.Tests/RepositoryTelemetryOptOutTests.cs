// Copyright (c) KeelMatrix

using System.IO;
using System.Text.Json;

namespace KeelMatrix.MetricBudget.Tests;

/// <summary>
/// Guards the mechanical opt-out that keeps this repository's own development, sample, and package-consumer runs
/// out of production demand data. The test host is covered by tests.runsettings; every other run path resolves the
/// committed repository configuration below, because git discovery finds the repository root from the running
/// process and the shared telemetry client honors that file.
/// </summary>
public sealed class RepositoryTelemetryOptOutTests
{
    private const string OptOutConfigurationFileName = "keelmatrix.telemetry.json";

    [Fact]
    public void RepositoryOptsEveryRunPathOutOfProductionTelemetry()
    {
        string configurationPath = Path.Combine(RepositoryRoot(), OptOutConfigurationFileName);

        Assert.True(
            File.Exists(configurationPath),
            "The repository-level telemetry opt-out is missing at " + configurationPath
                + ", so sample and package-consumer runs could enter production demand data.");

        using JsonDocument configuration = JsonDocument.Parse(File.ReadAllText(configurationPath));

        Assert.Equal(JsonValueKind.Object, configuration.RootElement.ValueKind);
        Assert.True(
            configuration.RootElement.TryGetProperty("disabled", out JsonElement disabled),
            "The repository telemetry configuration must set \"disabled\".");
        Assert.Equal(JsonValueKind.True, disabled.ValueKind);
    }

    [Fact]
    public void TelemetryDocumentationNamesTheMechanismThatCoversSampleAndConsumerRuns()
    {
        string documentationPath = Path.Combine(
            RepositoryRoot(),
            "docs",
            "privacy-and-telemetry.md");

        Assert.True(File.Exists(documentationPath), "Missing telemetry documentation at " + documentationPath + ".");

        string documentation = File.ReadAllText(documentationPath);

        Assert.Contains(OptOutConfigurationFileName, documentation, StringComparison.Ordinal);
        Assert.Contains("package-consumer", documentation, StringComparison.Ordinal);
        Assert.Contains("sample", documentation, StringComparison.Ordinal);
    }

    /// <summary>
    /// Walks up from the test host's base directory to the repository root, which is the directory that holds the
    /// committed telemetry configuration and the documented opt-out contract. The base directory is used instead of
    /// the assembly location because the .NET Framework host shadow-copies the test assembly before running it.
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
