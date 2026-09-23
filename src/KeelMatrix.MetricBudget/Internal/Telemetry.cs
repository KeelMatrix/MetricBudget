// Copyright (c) KeelMatrix

using System.Runtime.InteropServices;
using KeelMatrix.Telemetry;

namespace KeelMatrix.MetricBudget.Internal;

/// <summary>
/// Coarse outcome reported by the anonymous verification signal.
/// </summary>
internal enum MetricBudgetTelemetryOutcome
{
    Pass,
    Fail,
    InvalidConfiguration,
}

/// <summary>
/// Coarse operating-system family reported by the anonymous verification signal.
/// </summary>
internal enum MetricBudgetTelemetryOsFamily
{
    Windows,
    Linux,
    MacOS,
    Other,
}

/// <summary>
/// The complete set of aggregate fields a completed verification may ever report.
/// </summary>
/// <remarks>
/// <para>
/// This type is the allowlist. It is deliberately closed: every field is a coarse value derived from a count, an
/// enum, a target framework, or the package version. It has no member that can carry a meter name, instrument
/// name, tag key, tag value, metric value, URL, application name, or file path, and it has no free-form
/// dictionary a caller could extend.
/// </para>
/// <para>
/// <c>KeelMatrix.Telemetry</c> 0.1.1 exposes activation and heartbeat events with its own fixed schema and no
/// product-specific fields, so the aggregate signal is currently used to decide activation and heartbeat
/// semantics and to prove the allowlist, not to extend the transmitted payload.
/// </para>
/// </remarks>
internal sealed class MetricBudgetTelemetrySignal
{
    internal const string PackageVersionField = "package_version";
    internal const string TargetFrameworkField = "target_framework";
    internal const string OsFamilyField = "os_family";
    internal const string ObservedInstrumentBucketField = "observed_instrument_bucket";
    internal const string ConfiguredRuleCountField = "configured_rule_count";
    internal const string OutcomeField = "outcome";

    internal static readonly string[] AllowedFieldNames =
    {
        PackageVersionField,
        TargetFrameworkField,
        OsFamilyField,
        ObservedInstrumentBucketField,
        ConfiguredRuleCountField,
        OutcomeField,
    };

    private MetricBudgetTelemetrySignal(
        string packageVersion,
        string targetFramework,
        MetricBudgetTelemetryOsFamily osFamily,
        string observedInstrumentBucket,
        int configuredRuleCount,
        MetricBudgetTelemetryOutcome outcome)
    {
        PackageVersion = packageVersion;
        TargetFramework = targetFramework;
        OsFamily = osFamily;
        ObservedInstrumentBucket = observedInstrumentBucket;
        ConfiguredRuleCount = configuredRuleCount;
        Outcome = outcome;
    }

    internal string PackageVersion { get; }

    internal string TargetFramework { get; }

    internal MetricBudgetTelemetryOsFamily OsFamily { get; }

    internal string ObservedInstrumentBucket { get; }

    internal int ConfiguredRuleCount { get; }

    internal MetricBudgetTelemetryOutcome Outcome { get; }

    /// <summary>
    /// Creates the signal for a completed verification that observed at least one selected instrument.
    /// </summary>
    internal static MetricBudgetTelemetrySignal Create(MetricBudgetReport report)
    {
        return new MetricBudgetTelemetrySignal(
            CurrentPackageVersion,
            CurrentTargetFramework,
            CurrentOsFamily,
            Bucket(report.ObservedInstrumentCount),
            report.ConfiguredRuleCount,
            MapOutcome(report.Outcome));
    }

    /// <summary>
    /// Reports whether a field name may carry verification data.
    /// </summary>
    /// <param name="fieldName">Candidate field name.</param>
    /// <returns><see langword="true"/> when the name is on the allowlist.</returns>
    internal static bool IsAllowedFieldName(string fieldName)
    {
        for (int i = 0; i < AllowedFieldNames.Length; i++)
        {
            if (string.Equals(AllowedFieldNames[i], fieldName, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Projects the signal onto its fields. Every name is on the allowlist and every value is coarse.
    /// </summary>
    internal IReadOnlyList<KeyValuePair<string, string>> ToFields()
    {
        return new[]
        {
            new KeyValuePair<string, string>(PackageVersionField, PackageVersion),
            new KeyValuePair<string, string>(TargetFrameworkField, TargetFramework),
            new KeyValuePair<string, string>(OsFamilyField, OsFamilyToken(OsFamily)),
            new KeyValuePair<string, string>(ObservedInstrumentBucketField, ObservedInstrumentBucket),
            new KeyValuePair<string, string>(
                ConfiguredRuleCountField,
                ConfiguredRuleCount.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new KeyValuePair<string, string>(OutcomeField, OutcomeToken(Outcome)),
        };
    }

    /// <summary>
    /// Describes the signal as semicolon-separated <c>name=value</c> pairs.
    /// </summary>
    internal string Describe()
    {
        IReadOnlyList<KeyValuePair<string, string>> fields = ToFields();
        System.Text.StringBuilder builder = new System.Text.StringBuilder();
        for (int i = 0; i < fields.Count; i++)
        {
            if (i > 0)
            {
                builder.Append(';');
            }

            builder.Append(fields[i].Key).Append('=').Append(fields[i].Value);
        }

        return builder.ToString();
    }

    private static string CurrentPackageVersion =>
        typeof(MetricBudgetSession).Assembly.GetName().Version?.ToString() ?? "unknown";

#if NET8_0_OR_GREATER
    private const string CurrentTargetFramework = "net8.0";
#else
    private const string CurrentTargetFramework = "netstandard2.0";
#endif

    private static MetricBudgetTelemetryOsFamily CurrentOsFamily
    {
        get
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return MetricBudgetTelemetryOsFamily.Windows;
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                return MetricBudgetTelemetryOsFamily.Linux;
            }

            return RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
                ? MetricBudgetTelemetryOsFamily.MacOS
                : MetricBudgetTelemetryOsFamily.Other;
        }
    }

    private static string Bucket(int observedInstrumentCount)
    {
        if (observedInstrumentCount <= 0)
        {
            return "none";
        }

        if (observedInstrumentCount <= 5)
        {
            return "1-5";
        }

        if (observedInstrumentCount <= 25)
        {
            return "6-25";
        }

        return observedInstrumentCount <= 100 ? "26-100" : "101+";
    }

    private static MetricBudgetTelemetryOutcome MapOutcome(MetricBudgetOutcome outcome)
    {
        return outcome switch
        {
            MetricBudgetOutcome.Passed => MetricBudgetTelemetryOutcome.Pass,
            MetricBudgetOutcome.InvalidConfiguration => MetricBudgetTelemetryOutcome.InvalidConfiguration,
            _ => MetricBudgetTelemetryOutcome.Fail,
        };
    }

    private static string OsFamilyToken(MetricBudgetTelemetryOsFamily family)
    {
        return family switch
        {
            MetricBudgetTelemetryOsFamily.Windows => "windows",
            MetricBudgetTelemetryOsFamily.Linux => "linux",
            MetricBudgetTelemetryOsFamily.MacOS => "macos",
            _ => "other",
        };
    }

    private static string OutcomeToken(MetricBudgetTelemetryOutcome outcome)
    {
        return outcome switch
        {
            MetricBudgetTelemetryOutcome.Pass => "pass",
            MetricBudgetTelemetryOutcome.InvalidConfiguration => "invalid_configuration",
            _ => "fail",
        };
    }
}

/// <summary>
/// Receives the coarse verification signal.
/// </summary>
internal interface IMetricBudgetTelemetrySink
{
    void Report(MetricBudgetTelemetrySignal signal);
}

/// <summary>
/// Default sink: the shared KeelMatrix telemetry client.
/// </summary>
/// <remarks>
/// The first completed verification that observed at least one instrument requests activation and heartbeat
/// eligibility; later completions request heartbeat eligibility. The shared client suppresses a duplicate activation,
/// suppresses a heartbeat in the activation week, and emits at most one heartbeat per project and ISO week. The
/// client is created lazily on first use, so installing, restoring, or loading the assembly never reports anything.
/// </remarks>
internal sealed class KeelMatrixTelemetrySink : IMetricBudgetTelemetrySink
{
    internal const string ToolName = "MetricBudget";

    private readonly Action trackActivation;
    private readonly Action trackHeartbeat;

    /// <summary>Creates a sink backed by the shared KeelMatrix telemetry client.</summary>
    internal KeelMatrixTelemetrySink()
    {
        Lazy<Client> client = new(
            static () => new Client(ToolName, typeof(MetricBudgetSession)),
            LazyThreadSafetyMode.ExecutionAndPublication);
        trackActivation = () => client.Value.TrackActivation();
        trackHeartbeat = () => client.Value.TrackHeartbeat();
    }

    /// <summary>Creates a sink with injectable request actions for isolated behavior tests.</summary>
    /// <param name="trackActivation">Action that requests activation eligibility.</param>
    /// <param name="trackHeartbeat">Action that requests heartbeat eligibility.</param>
    internal KeelMatrixTelemetrySink(Action trackActivation, Action trackHeartbeat)
    {
        this.trackActivation = trackActivation ?? throw new ArgumentNullException(nameof(trackActivation));
        this.trackHeartbeat = trackHeartbeat ?? throw new ArgumentNullException(nameof(trackHeartbeat));
    }

    private int reported;

    public void Report(MetricBudgetTelemetrySignal signal)
    {
        if (Interlocked.CompareExchange(ref reported, 1, 0) == 0)
        {
            trackActivation();
            trackHeartbeat();
        }
        else
        {
            trackHeartbeat();
        }
    }
}

/// <summary>
/// Telemetry boundary for completed verifications.
/// </summary>
/// <remarks>
/// Telemetry is best effort and never a dependency of a verification: every failure is swallowed, and no caller
/// changes a report because of it.
/// </remarks>
internal static class MetricBudgetTelemetry
{
    private static readonly IMetricBudgetTelemetrySink DefaultSink = new KeelMatrixTelemetrySink();

    private static IMetricBudgetTelemetrySink? sinkOverride;

    /// <summary>
    /// Replaces the sink so a test can inspect the signal without touching the shared client.
    /// </summary>
    internal static void SetSinkForTests(IMetricBudgetTelemetrySink? sink)
    {
        Volatile.Write(ref sinkOverride, sink);
    }

    internal static void ReportCompleted(MetricBudgetTelemetrySignal signal)
    {
        try
        {
            (Volatile.Read(ref sinkOverride) ?? DefaultSink).Report(signal);
        }
        catch
        {
            // Telemetry must never change, delay, or fail a verification result.
        }
    }
}
