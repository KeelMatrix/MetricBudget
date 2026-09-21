// Copyright (c) KeelMatrix

using System.Diagnostics;
using System.IO;

namespace KeelMatrix.MetricBudget.Tests;

/// <summary>
/// Guards the mechanical opt-out that keeps this repository's own development, sample, and package-consumer runs
/// out of production demand data. The test host is covered by tests.runsettings; the other repository-owned entry
/// points set the supported process-level opt-out explicitly.
/// </summary>
public sealed class RepositoryTelemetryOptOutTests
{
    [Fact]
    public void RepositoryTelemetryConfigurationIsIgnoredAndUntracked()
    {
        string root = RepositoryRoot();
        string ignorePath = Path.Combine(root, ".gitignore");

        Assert.Contains("keelmatrix.telemetry.json", File.ReadAllText(ignorePath), StringComparison.Ordinal);

        ProcessStartInfo startInfo = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = root,
            Arguments = "ls-files -- keelmatrix.telemetry.json",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start git to verify telemetry configuration tracking.");
        string output = process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        Assert.True(process.ExitCode == 0, "git ls-files failed: " + error);
        Assert.Equal(string.Empty, output.Trim());
    }

    [Fact]
    public void RepositoryOwnedEntryPointsSetTheSupportedOptOut()
    {
        string root = RepositoryRoot();
        string runSettings = File.ReadAllText(Path.Combine(root, "tests", "KeelMatrix.MetricBudget.Tests", "tests.runsettings"));
        string development = File.ReadAllText(Path.Combine(root, "docs", "DEV.md"));
        string sample = File.ReadAllText(Path.Combine(root, "samples", "KeelMatrix.MetricBudget.Sample", "Program.cs"));
        string consumer = File.ReadAllText(Path.Combine(root, "tests", "KeelMatrix.MetricBudget.PackageConsumer", "Program.cs"));
        string packageGate = File.ReadAllText(Path.Combine(root, "scripts", "verify-package.ps1"));

        Assert.Contains("<KEELMATRIX_NO_TELEMETRY>1</KEELMATRIX_NO_TELEMETRY>", runSettings, StringComparison.Ordinal);
        Assert.Contains("$env:KEELMATRIX_NO_TELEMETRY = \"1\"", development, StringComparison.Ordinal);
        Assert.Contains("Environment.SetEnvironmentVariable(\"KEELMATRIX_NO_TELEMETRY\", \"1\")", sample, StringComparison.Ordinal);
        Assert.Contains("Environment.SetEnvironmentVariable(\"KEELMATRIX_NO_TELEMETRY\", \"1\")", consumer, StringComparison.Ordinal);
        Assert.Contains("$env:KEELMATRIX_NO_TELEMETRY = \"1\"", packageGate, StringComparison.Ordinal);
    }

    /// <summary>
    /// Walks up from the test host's base directory to the repository root. The base directory is used instead of the
    /// assembly location because the .NET Framework host shadow-copies the test assembly before running it.
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
