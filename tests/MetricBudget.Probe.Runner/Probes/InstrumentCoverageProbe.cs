using System.Diagnostics.Metrics;
using System.Globalization;
using System.Reflection;

namespace MetricBudget.Probe.Runner.Probes;

/// <summary>
/// Instrument coverage matrix: every instrument kind crossed with every candidate measurement type.
/// </summary>
internal static class InstrumentCoverageProbe
{
    private static readonly string[] Kinds =
    {
        "Counter", "UpDownCounter", "Histogram", "ObservableCounter", "ObservableUpDownCounter", "ObservableGauge",
    };

    private static readonly Type[] NumericTypes =
    {
        typeof(byte), typeof(short), typeof(int), typeof(long), typeof(float), typeof(double), typeof(decimal),
    };

    private static readonly Type[] NonNumericTypes =
    {
        typeof(char), typeof(bool), typeof(TimeSpan), typeof(DateTime), typeof(Int128), typeof(SampleState),
        typeof(int?),
    };

    private static readonly KeyValuePair<string, object?>[] ObservableTags =
    {
        new KeyValuePair<string, object?>("probe.kind", "observable"),
    };

    public static ProbeSectionResult Run()
    {
        ProbeReport.Section("1.1 instrument coverage matrix");
        ProbeReport.Line("row format: kind; T; creation; instrumentType; isObservable; enabled; callbackTypes; deliveredMeasurementTypes; deliveredTagCount; exception");

        ProbeSectionResult result = new ProbeSectionResult("instrument coverage matrix");
        Dictionary<string, int> deliveredByKind = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (string kind in Kinds)
        {
            foreach (Type measurementType in NumericTypes)
            {
                string row = RunCase(kind, measurementType, out bool delivered);
                ProbeReport.Line("  " + row);
                if (delivered)
                {
                    deliveredByKind[kind] = deliveredByKind.TryGetValue(kind, out int seen) ? seen + 1 : 1;
                }
            }
        }

        ProbeReport.Section("1.2 unsupported measurement types");
        foreach (string kind in Kinds)
        {
            foreach (Type measurementType in NonNumericTypes)
            {
                ProbeReport.Line("  " + RunCase(kind, measurementType, out _));
            }
        }

        ProbeReport.Section("1.3 instrument measurement surface");
        DescribeInstrumentSurface();

        foreach (string kind in Kinds)
        {
            int delivered = deliveredByKind.TryGetValue(kind, out int count) ? count : 0;
            result.Add(
                kind + " numeric measurement types",
                delivered == NumericTypes.Length ? ProbeVerdict.Pass : ProbeVerdict.Fail,
                delivered.ToString(CultureInfo.InvariantCulture) + "/" + NumericTypes.Length.ToString(CultureInfo.InvariantCulture)
                    + " numeric measurement types produced a delivered callback with the exact measurement type");
        }

        return result;
    }

    private static string RunCase(string kind, Type measurementType, out bool delivered)
    {
        delivered = false;
        string typeToken = TypeToken(measurementType);
        string meterName = "probe.coverage." + kind.ToLowerInvariant() + "." + typeToken;
        const string instrumentName = "probe.instrument";

        using Meter meter = new Meter(meterName, "1.0.0");
        using MeterObservationSession session = new MeterObservationSession(
            meterName,
            instrument => string.Equals(instrument.Meter.Name, meterName, StringComparison.Ordinal),
            seriesCap: 16,
            tagValueCapPerKey: 16,
            tagKeyCap: 8);

        object? instrument = null;
        string creation = "created";
        string exception = "-";

        try
        {
            instrument = CreateInstrument(meter, kind, measurementType, instrumentName);
        }
        catch (Exception ex)
        {
            creation = "creation-failed";
            exception = Describe(ex);
        }

        session.Start();

        if (instrument is null)
        {
            return Join(kind, typeToken, creation, "-", "-", "-", "-", "-", "-", exception);
        }

        Instrument typed = (Instrument)instrument;

        try
        {
            if (IsObservable(kind))
            {
                session.RecordObservableInstruments();
            }
            else
            {
                RecordMeasurement(instrument, measurementType, kind);
            }
        }
        catch (Exception ex)
        {
            exception = Describe(ex);
        }

        IReadOnlyDictionary<string, long> callbacks = session.CallbackCounts;
        string callbackTypes = callbacks.Count == 0
            ? "none"
            : string.Join("+", callbacks.Select(pair => pair.Key + "=" + pair.Value.ToString(CultureInfo.InvariantCulture)));

        string deliveredTypes = session.LastMeasurementType ?? "none";
        ProbeObservationSummary summary = session.Summarize();
        string deliveredTags = summary.ObservedMeasurements.ToString(CultureInfo.InvariantCulture);
        delivered = callbacks.Count > 0 && summary.ObservedMeasurements > 0;

        return Join(
            kind,
            typeToken,
            creation,
            typed.GetType().Name,
            typed.IsObservable.ToString(CultureInfo.InvariantCulture),
            typed.Enabled.ToString(CultureInfo.InvariantCulture),
            callbackTypes,
            deliveredTypes,
            deliveredTags,
            exception);
    }

    private static string Join(
        string kind,
        string typeToken,
        string creation,
        string instrumentType,
        string isObservable,
        string enabled,
        string callbackTypes,
        string deliveredTypes,
        string deliveredTagCount,
        string exception)
    {
        return string.Join(
            "; ",
            "kind=" + kind,
            "T=" + typeToken,
            "creation=" + creation,
            "instrumentType=" + instrumentType,
            "isObservable=" + isObservable,
            "enabled=" + enabled,
            "callbacks=" + callbackTypes,
            "deliveredMeasurementType=" + deliveredTypes,
            "deliveredTagSets=" + deliveredTagCount,
            "exception=" + exception);
    }

    private static void DescribeInstrumentSurface()
    {
        Type instrument = typeof(Instrument);
        string[] members = instrument
            .GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Select(member => member.MemberType + " " + member.Name)
            .OrderBy(text => text, StringComparer.Ordinal)
            .ToArray();

        ProbeReport.Line("  Instrument public members: " + string.Join(", ", members));

        PropertyInfo? measure = instrument.GetProperty("Measure", BindingFlags.Public | BindingFlags.Instance);
        PropertyInfo? anyMeasure = instrument.GetProperty(
            "Measure",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

        ProbeReport.Line(
            "  Instrument.Measure: public=" + ProbeReport.Format(measure is not null)
            + "; declared-or-nonpublic=" + ProbeReport.Format(anyMeasure is not null)
            + "; declaredType=" + (anyMeasure?.PropertyType.FullName ?? "<none>"));

        MethodInfo[] recorders = typeof(Instrument<>)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(method => method.Name == "RecordMeasurement")
            .ToArray();

        ProbeReport.Line(
            "  Instrument<T>.RecordMeasurement public overloads: "
            + (recorders.Length == 0
                ? "none"
                : string.Join(
                    " | ",
                    recorders.Select(method => string.Join(
                        ", ",
                        method.GetParameters().Select(parameter => parameter.ParameterType.Name))))));

        string[] declaredMembers = typeof(Instrument<>)
            .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Select(method => method.Name)
            .Distinct()
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        ProbeReport.Line("  Instrument<T> declared members (any accessibility): " + string.Join(", ", declaredMembers));
        ProbeReport.Line(
            "  conclusion: net8.0 exposes no public Measure or RecordMeasurement member on Instrument or "
            + "Instrument<T>; measurements are observable only through SetMeasurementEventCallback<T>");
    }

    private static object CreateInstrument(Meter meter, string kind, Type measurementType, string instrumentName)
    {
        bool observable = IsObservable(kind);
        MethodInfo create = SelectCreateMethod(kind, observable);

        object?[] arguments;
        if (observable)
        {
            Type delegateType = typeof(Func<>).MakeGenericType(
                typeof(IEnumerable<>).MakeGenericType(typeof(Measurement<>).MakeGenericType(measurementType)));

            MethodInfo factory = typeof(InstrumentCoverageProbe)
                .GetMethod(nameof(ObserveSingleMeasurement), BindingFlags.NonPublic | BindingFlags.Static)!
                .MakeGenericMethod(measurementType);

            arguments = new object?[] { instrumentName, Delegate.CreateDelegate(delegateType, factory), null, null, null };
        }
        else
        {
            arguments = new object?[] { instrumentName, null, null, null };
        }

        MethodInfo closed = create.MakeGenericMethod(measurementType);
        ParameterInfo[] parameters = closed.GetParameters();
        object?[] trimmed = new object?[parameters.Length];
        for (int i = 0; i < parameters.Length; i++)
        {
            trimmed[i] = i < arguments.Length ? arguments[i] : null;
        }

        try
        {
            return closed.Invoke(meter, trimmed)!;
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            throw ex.InnerException;
        }
    }

    private static MethodInfo SelectCreateMethod(string kind, bool observable)
    {
        string name = "Create" + kind;
        List<MethodInfo> candidates = typeof(Meter)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(method => method.Name == name && method.IsGenericMethodDefinition)
            .ToList();

        if (observable)
        {
            MethodInfo? multi = candidates.FirstOrDefault(method =>
            {
                ParameterInfo[] parameters = method.GetParameters();
                if (parameters.Length < 2 || !parameters[1].ParameterType.IsGenericType)
                {
                    return false;
                }

                Type argument = parameters[1].ParameterType.GetGenericArguments()[0];
                return argument.IsGenericType && argument.GetGenericTypeDefinition() == typeof(IEnumerable<>);
            });

            return multi ?? candidates.OrderByDescending(method => method.GetParameters().Length).First();
        }

        return candidates.OrderByDescending(method => method.GetParameters().Length).First();
    }

    private static void RecordMeasurement(object instrument, Type measurementType, string kind)
    {
        Type instrumentType = instrument.GetType();
        MethodInfo? method = instrumentType
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(candidate => candidate.Name is "Add" or "Record")
            .Where(candidate => candidate.GetParameters().Length > 0
                && candidate.GetParameters()[0].ParameterType == measurementType)
            .OrderByDescending(candidate =>
            {
                ParameterInfo[] parameters = candidate.GetParameters();
                if (parameters.Length == 2 && parameters[1].ParameterType.IsGenericType
                    && parameters[1].ParameterType.GetGenericTypeDefinition() == typeof(KeyValuePair<,>))
                {
                    return 3;
                }

                return parameters.Length == 1 ? 2 : 1;
            })
            .First();

        object? value = measurementType.IsValueType ? Activator.CreateInstance(measurementType) : null;
        ParameterInfo[] selected = method.GetParameters();
        object?[] arguments = new object?[selected.Length];
        arguments[0] = value;
        for (int i = 1; i < selected.Length; i++)
        {
            arguments[i] = selected[i].ParameterType == typeof(KeyValuePair<string, object?>)
                ? new KeyValuePair<string, object?>("probe.kind", kind)
                : null;
        }

        try
        {
            method.Invoke(instrument, arguments);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            throw ex.InnerException;
        }
    }

#pragma warning disable CA1859 // the observable factory delegate requires Func<IEnumerable<Measurement<T>>>
    private static IEnumerable<Measurement<T>> ObserveSingleMeasurement<T>()
        where T : struct
    {
        return new[] { new Measurement<T>(default, ObservableTags) };
    }
#pragma warning restore CA1859

    private static bool IsObservable(string kind) => kind.StartsWith("Observable", StringComparison.Ordinal);

    private static string TypeToken(Type type)
    {
        if (type == typeof(byte))
        {
            return "byte";
        }

        if (type == typeof(short))
        {
            return "short";
        }

        if (type == typeof(int))
        {
            return "int";
        }

        if (type == typeof(long))
        {
            return "long";
        }

        if (type == typeof(float))
        {
            return "float";
        }

        if (type == typeof(double))
        {
            return "double";
        }

        if (type == typeof(decimal))
        {
            return "decimal";
        }

        if (type == typeof(int?))
        {
            return "int-nullable";
        }

        return type.Name;
    }

    private static string Describe(Exception exception)
    {
        Exception root = exception;
        while (root.InnerException is not null)
        {
            root = root.InnerException;
        }

        return root.GetType().FullName + ": " + root.Message.Replace('\n', ' ').Replace('\r', ' ');
    }

    private enum SampleState
    {
        None,
        Active,
    }
}
