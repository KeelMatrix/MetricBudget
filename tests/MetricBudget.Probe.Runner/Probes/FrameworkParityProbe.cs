using System.Diagnostics.Metrics;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using MetricBudget.Probe.NetStandard;

namespace MetricBudget.Probe.Runner.Probes;

/// <summary>
/// netstandard2.0 asset presence, API parity against the net8.0 framework, runtime interop, and dependency closure.
/// </summary>
internal static class FrameworkParityProbe
{
    private static readonly string[] MetricsTypeNames =
    {
        "System.Diagnostics.Metrics.MeterListener",
        "System.Diagnostics.Metrics.Meter",
        "System.Diagnostics.Metrics.Instrument",
        "System.Diagnostics.Metrics.Instrument`1",
        "System.Diagnostics.Metrics.Counter`1",
        "System.Diagnostics.Metrics.Histogram`1",
        "System.Diagnostics.Metrics.UpDownCounter`1",
        "System.Diagnostics.Metrics.ObservableCounter`1",
        "System.Diagnostics.Metrics.ObservableUpDownCounter`1",
        "System.Diagnostics.Metrics.ObservableGauge`1",
        "System.Diagnostics.Metrics.Measurement`1",
        "System.Diagnostics.Metrics.MeasurementCallback`1",
        "System.Diagnostics.TagList",
    };

    private static readonly Type[] FrameworkTypes =
    {
        typeof(MeterListener),
        typeof(Meter),
        typeof(Instrument),
        typeof(Instrument<>),
        typeof(Counter<>),
        typeof(Histogram<>),
        typeof(UpDownCounter<>),
        typeof(ObservableCounter<>),
        typeof(ObservableUpDownCounter<>),
        typeof(ObservableGauge<>),
        typeof(Measurement<>),
        typeof(MeasurementCallback<>),
        typeof(System.Diagnostics.TagList),
    };

    public static ProbeSectionResult Run()
    {
        ProbeSectionResult result = new ProbeSectionResult("netstandard2.0 decision");
        PackageAssetCatalogue(result);
        SurfaceDiff(result);
        RuntimeInterop(result);
        DependencyClosure(result);
        return result;
    }

    private static void PackageAssetCatalogue(ProbeSectionResult result)
    {
        ProbeReport.Section("6.1 System.Diagnostics.DiagnosticSource netstandard2.0 assets by package version");

        string packageRoot = Path.Combine(NuGetPackagesRoot(), "system.diagnostics.diagnosticsource");
        ProbeReport.KeyValue("packageCacheRoot", packageRoot);

        if (!Directory.Exists(packageRoot))
        {
            ProbeReport.Line("  package cache not present; version catalogue unavailable on this host");
            result.Add("version catalogue", ProbeVerdict.Narrow, "package cache not present");
            return;
        }

        string? minimumRequired = null;
        string? minimumListener = null;
        string? minimumTagList = null;
        string? minimumCallback = null;
        string? minimumInstrumentTags = null;

        string[] versions = Directory
            .GetDirectories(packageRoot)
            .Select(directory => Path.GetFileName(directory))
            .Where(version => !string.IsNullOrEmpty(version))
            .OrderBy(version => ParseVersion(version!))
            .ToArray();

        foreach (string version in versions)
        {
            string asset = Path.Combine(packageRoot, version, "lib", "netstandard2.0", "System.Diagnostics.DiagnosticSource.dll");
            if (!File.Exists(asset))
            {
                ProbeReport.Line("  " + version + ": no netstandard2.0 asset");
                continue;
            }

            try
            {
                Assembly assembly = Assembly.Load(File.ReadAllBytes(asset));
                Type? listener = assembly.GetType("System.Diagnostics.Metrics.MeterListener", throwOnError: false);
                bool hasTagList = assembly.GetType("System.Diagnostics.TagList", throwOnError: false) is not null;
                int callbackArity = CallbackArity(assembly);
                bool hasInstrumentTags = assembly
                    .GetType("System.Diagnostics.Metrics.Instrument", throwOnError: false)?
                    .GetProperty("Tags", BindingFlags.Public | BindingFlags.Instance) is not null;

                bool complete = listener is not null && hasTagList && callbackArity == 4 && hasInstrumentTags;
                if (listener is not null)
                {
                    minimumListener ??= version;
                }

                if (hasTagList)
                {
                    minimumTagList ??= version;
                }

                if (callbackArity == 4)
                {
                    minimumCallback ??= version;
                }

                if (hasInstrumentTags)
                {
                    minimumInstrumentTags ??= version;
                }

                if (complete && minimumRequired is null)
                {
                    minimumRequired = version;
                }

                ProbeReport.Line(
                    "  " + version + ": asset=netstandard2.0"
                    + "; MeterListener=" + ProbeReport.Format(listener is not null)
                    + "; TagList=" + ProbeReport.Format(hasTagList)
                    + "; Instrument.Tags=" + ProbeReport.Format(hasInstrumentTags)
                    + "; MeasurementCallback<T>.Invoke parameters=" + callbackArity.ToString(CultureInfo.InvariantCulture)
                    + "; complete=" + ProbeReport.Format(complete));
            }
            catch (Exception ex)
            {
                ProbeReport.Line("  " + version + ": reflection failed: " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        ProbeReport.KeyValue("firstVersionWithMeterListener", minimumListener ?? "<none>");
        ProbeReport.KeyValue("firstVersionWithTagList", minimumTagList ?? "<none>");
        ProbeReport.KeyValue("firstVersionWithFourArgumentCallback", minimumCallback ?? "<none>");
        ProbeReport.KeyValue("firstVersionWithInstrumentTags", minimumInstrumentTags ?? "<none>");
        ProbeReport.KeyValue("firstVersionWithFullRequiredSurface", minimumRequired ?? "<none>");
        ProbeReport.KeyValue("pinnedPackageVersion", PinnedPackageVersion() ?? "<unresolved>");

        result.Add(
            "netstandard2.0 asset exposes the required surface",
            minimumRequired is not null ? ProbeVerdict.Pass : ProbeVerdict.Fail,
            minimumRequired is null
                ? "no cached netstandard2.0 asset exposes MeterListener with the four-argument measurement callback"
                : "netstandard2.0 assets exist from " + (minimumListener ?? "<none>")
                    + "; the four-argument measurement callback starts at " + (minimumCallback ?? "<none>")
                    + " and Instrument.Tags at " + (minimumInstrumentTags ?? "<none>")
                    + ", so the full required surface starts at " + minimumRequired);
    }

    private static void SurfaceDiff(ProbeSectionResult result)
    {
        ProbeReport.Section("6.2 API parity: net8.0 framework surface versus netstandard2.0 package asset");

        string version = PinnedPackageVersion() ?? "8.0.1";
        string asset = Path.Combine(
            NuGetPackagesRoot(),
            "system.diagnostics.diagnosticsource",
            version,
            "lib",
            "netstandard2.0",
            "System.Diagnostics.DiagnosticSource.dll");

        if (!File.Exists(asset))
        {
            ProbeReport.Line("  asset not available: " + asset);
            result.Add("API parity", ProbeVerdict.Narrow, "netstandard2.0 asset for the pinned version is not present in the package cache");
            return;
        }

        Assembly packageAssembly = Assembly.Load(File.ReadAllBytes(asset));
        ProbeReport.KeyValue("comparedAsset", asset);
        ProbeReport.KeyValue("comparedAssetIdentity", packageAssembly.FullName);
        ProbeReport.KeyValue("comparedAssetAssemblyVersion", packageAssembly.GetName().Version);
        ProbeReport.KeyValue("frameworkAssemblyIdentity", typeof(MeterListener).Assembly.FullName);

        int missingTotal = 0;
        int extraTotal = 0;

        for (int i = 0; i < MetricsTypeNames.Length; i++)
        {
            Type? packageType = packageAssembly.GetType(MetricsTypeNames[i], throwOnError: false);
            if (packageType is null)
            {
                ProbeReport.Line("  " + MetricsTypeNames[i] + ": missing on netstandard2.0");
                missingTotal++;
                continue;
            }

            string[] frameworkSurface = Surface(FrameworkTypes[i]);
            string[] packageSurface = Surface(packageType);

            string[] missing = frameworkSurface.Except(packageSurface, StringComparer.Ordinal).ToArray();
            string[] extra = packageSurface.Except(frameworkSurface, StringComparer.Ordinal).ToArray();
            missingTotal += missing.Length;
            extraTotal += extra.Length;

            ProbeReport.Line(
                "  " + MetricsTypeNames[i] + ": frameworkMembers=" + frameworkSurface.Length.ToString(CultureInfo.InvariantCulture)
                + "; netstandardMembers=" + packageSurface.Length.ToString(CultureInfo.InvariantCulture)
                + "; missing=" + missing.Length.ToString(CultureInfo.InvariantCulture)
                + "; extra=" + extra.Length.ToString(CultureInfo.InvariantCulture));

            foreach (string entry in missing.Take(8))
            {
                ProbeReport.Line("      missing: " + entry);
            }

            foreach (string entry in extra.Take(8))
            {
                ProbeReport.Line("      extra: " + entry);
            }
        }

        ProbeReport.KeyValue("totalMissingMembers", missingTotal);
        ProbeReport.KeyValue("totalExtraMembers", extraTotal);

        result.Add(
            "API parity on the public surface",
            missingTotal == 0 ? ProbeVerdict.Pass : ProbeVerdict.Narrow,
            missingTotal.ToString(CultureInfo.InvariantCulture)
                + " public members present on net8.0 are absent on the netstandard2.0 asset; "
                + extraTotal.ToString(CultureInfo.InvariantCulture) + " netstandard2.0-only members");
    }

    private static void RuntimeInterop(ProbeSectionResult result)
    {
        ProbeReport.Section("6.3 netstandard2.0-compiled session observing net8.0 measurements");

        const string meterName = "probe.parity.netstandard";
        string detail;
        bool passed = false;

        try
        {
            using Meter meter = new Meter(meterName, "1.0.0");
            Counter<long> counter = meter.CreateCounter<long>("probe.parity.counter");
            int observableInvocations = 0;
            meter.CreateObservableCounter<long>(
                "probe.parity.observable",
                () =>
                {
                    observableInvocations++;
                    return observableInvocations;
                });

            using NetStandardObservationSession session = new NetStandardObservationSession(
                seriesCap: 256,
                tagValueCapPerKey: 64,
                tagKeyCap: 8);

            session.Start();

            for (int i = 0; i < 5; i++)
            {
                counter.Add(1, new KeyValuePair<string, object?>("route", "/p" + i.ToString(CultureInfo.InvariantCulture)));
            }

            NetStandardObservationSession.AddWithTagList(counter, 1, "route", "/taglist");
            ProbeReport.KeyValue("instrumentEnabledViaNetStandardApi", NetStandardObservationSession.IsEnabled(counter));

            session.RecordObservableInstruments();

            ProbeReport.KeyValue("netstandardSessionDescription", session.Describe());
            ProbeReport.KeyValue("netstandardCallbackCounts", string.Join(", ", session.CallbackCounts.Select(pair => pair.Key + "=" + pair.Value.ToString(CultureInfo.InvariantCulture))));
            ProbeReport.KeyValue("netstandardPublishedInstruments", session.PublishedInstruments.Count);
            ProbeReport.KeyValue("observableInvocations", observableInvocations);

            passed = session.ObservedMeasurements == 7
                && session.TrackedSeriesCount == 7
                && observableInvocations == 1
                && session.CopiedTagSets == 7;

            detail = "observed=" + session.ObservedMeasurements
                + "; trackedSeries=" + session.TrackedSeriesCount
                + "; copiedTagSets=" + session.CopiedTagSets
                + "; observableInvocations=" + observableInvocations;
        }
        catch (Exception ex)
        {
            detail = ex.GetType().FullName + ": " + ex.Message;
        }

        ProbeReport.Line("  " + detail);

        result.Add(
            "netstandard2.0-compiled listener code runs against the net8.0 runtime",
            passed ? ProbeVerdict.Pass : ProbeVerdict.Fail,
            detail + " (the netstandard2.0 assembly binds to the framework System.Diagnostics.DiagnosticSource 8.0.0.0 "
                + "because the assembly identity matches; a netstandard2.0 host was not available for this probe)");
    }

    private static void DependencyClosure(ProbeSectionResult result)
    {
        ProbeReport.Section("6.4 dependency closure for a netstandard2.0 consumer");

        string version = PinnedPackageVersion() ?? "8.0.1";
        string nuspec = Path.Combine(
            NuGetPackagesRoot(),
            "system.diagnostics.diagnosticsource",
            version,
            "system.diagnostics.diagnosticsource.nuspec");

        List<string> direct = new List<string>();
        if (File.Exists(nuspec))
        {
            XDocument document = XDocument.Load(nuspec);
            XNamespace ns = document.Root?.Name.Namespace ?? XNamespace.None;
            XElement? group = document
                .Descendants(ns + "group")
                .FirstOrDefault(element => string.Equals(
                    (string?)element.Attribute("targetFramework"),
                    ".NETStandard2.0",
                    StringComparison.OrdinalIgnoreCase));

            if (group is not null)
            {
                foreach (XElement dependency in group.Elements(ns + "dependency"))
                {
                    direct.Add((string?)dependency.Attribute("id") + " " + (string?)dependency.Attribute("version"));
                }
            }
        }

        ProbeReport.KeyValue("nuspecPath", nuspec);
        ProbeReport.KeyValue("netstandard2.0DirectDependencies", direct.Count == 0 ? "<none>" : string.Join(", ", direct));

        List<string> closure = new List<string>();
        string assetsPath = Path.Combine(
            RepositoryRoot(),
            "src",
            "MetricBudget.Probe.NetStandard",
            "obj",
            "project.assets.json");

        if (File.Exists(assetsPath))
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(assetsPath));
            if (document.RootElement.TryGetProperty("targets", out JsonElement targets))
            {
                foreach (JsonProperty target in targets.EnumerateObject())
                {
                    bool isNetStandardTarget = string.Equals(target.Name, "netstandard2.0", StringComparison.Ordinal)
                        || target.Name.Contains(".NETStandard,Version=v2.0", StringComparison.Ordinal);
                    if (!isNetStandardTarget)
                    {
                        continue;
                    }

                    foreach (JsonProperty library in target.Value.EnumerateObject())
                    {
                        if (library.Value.TryGetProperty("type", out JsonElement type)
                            && string.Equals(type.GetString(), "package", StringComparison.Ordinal))
                        {
                            closure.Add(library.Name);
                        }
                    }
                }
            }
        }

        ProbeReport.KeyValue("assetsPath", assetsPath);
        ProbeReport.KeyValue("assetsFileExists", File.Exists(assetsPath));
        ProbeReport.KeyValue("netstandard2.0TransitiveClosure", closure.Count == 0 ? "<none>" : string.Join(", ", closure.OrderBy(entry => entry, StringComparer.Ordinal)));
        ProbeReport.KeyValue("netstandard2.0TransitivePackageCount", closure.Count);

        result.Add(
            "netstandard2.0 dependency burden",
            closure.Count <= 3 ? ProbeVerdict.Pass : closure.Count <= 8 ? ProbeVerdict.Narrow : ProbeVerdict.Fail,
            "the netstandard2.0 target adds " + direct.Count.ToString(CultureInfo.InvariantCulture)
                + " direct and " + closure.Count.ToString(CultureInfo.InvariantCulture)
                + " total resolved packages to the consumer graph: "
                + (closure.Count == 0 ? "<none>" : string.Join(", ", closure.OrderBy(entry => entry, StringComparer.Ordinal))));
    }

    private static string? PinnedPackageVersion()
    {
        string props = Path.Combine(RepositoryRoot(), "Directory.Packages.props");
        if (!File.Exists(props))
        {
            return null;
        }

        Match match = Regex.Match(
            File.ReadAllText(props),
            "System\\.Diagnostics\\.DiagnosticSource\"\\s+Version=\"([^\"]+)\"",
            RegexOptions.CultureInvariant);

        return match.Success ? match.Groups[1].Value : null;
    }

    private static string RepositoryRoot()
    {
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
    }

    private static string NuGetPackagesRoot()
    {
        string? configured = Environment.GetEnvironmentVariable("NUGET_PACKAGES");
        if (!string.IsNullOrEmpty(configured))
        {
            return configured;
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".nuget",
            "packages");
    }

    private static Version ParseVersion(string text)
    {
        string numeric = text.Split('-')[0];
        return Version.TryParse(numeric, out Version? parsed) ? parsed : new Version(0, 0);
    }

    private static int CallbackArity(Assembly assembly)
    {
        Type? callback = assembly.GetType("System.Diagnostics.Metrics.MeasurementCallback`1", throwOnError: false);
        MethodInfo? invoke = callback?.GetMethod("Invoke");
        return invoke?.GetParameters().Length ?? -1;
    }

    private static string[] Surface(Type type)
    {
        return type
            .GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Select(Signature)
            .OrderBy(text => text, StringComparer.Ordinal)
            .ToArray();
    }

    private static string Signature(MemberInfo member)
    {
        try
        {
            switch (member)
            {
                case MethodInfo method:
                    string generics = method.IsGenericMethodDefinition
                        ? "<" + method.GetGenericArguments().Length.ToString(CultureInfo.InvariantCulture) + ">"
                        : string.Empty;
                    return "M " + method.Name + generics
                        + "(" + string.Join(",", method.GetParameters().Select(parameter => TypeName(parameter.ParameterType))) + ")"
                        + " -> " + TypeName(method.ReturnType);
                case PropertyInfo property:
                    return "P " + property.Name + " : " + TypeName(property.PropertyType);
                case ConstructorInfo constructor:
                    return "C (" + string.Join(",", constructor.GetParameters().Select(parameter => TypeName(parameter.ParameterType))) + ")";
                case FieldInfo field:
                    return "F " + field.Name + " : " + TypeName(field.FieldType);
                default:
                    return member.MemberType + " " + member.Name;
            }
        }
        catch (Exception ex)
        {
            return member.MemberType + " " + member.Name + " <unresolved: " + ex.GetType().Name + ">";
        }
    }

    private static string TypeName(Type type)
    {
        if (type.IsByRef)
        {
            Type? element = type.GetElementType();
            return element is null ? type.Name + "&" : TypeName(element) + "&";
        }

        if (type.IsArray)
        {
            Type? element = type.GetElementType();
            return element is null ? type.Name : TypeName(element) + "[]";
        }

        if (type.IsGenericParameter)
        {
            return type.Name;
        }

        if (type.IsGenericType)
        {
            string name = type.GetGenericTypeDefinition().FullName ?? type.Name;
            int tick = name.IndexOf('`', StringComparison.Ordinal);
            if (tick >= 0)
            {
                name = name.Substring(0, tick);
            }

            return name + "<" + string.Join(",", type.GetGenericArguments().Select(TypeName)) + ">";
        }

        return type.FullName ?? type.Name;
    }
}
