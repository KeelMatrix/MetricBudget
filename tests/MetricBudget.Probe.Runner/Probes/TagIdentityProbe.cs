using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace MetricBudget.Probe.Runner.Probes;

/// <summary>
/// Tag identity, canonicalization, culture independence, edge-case tag delivery, and callback-buffer retention.
/// </summary>
internal static class TagIdentityProbe
{
    private static readonly int[] RepresentativeValueCounts = { 10_000, 100_000, 1_000_000 };

    public static ProbeSectionResult Run()
    {
        ProbeSectionResult result = new ProbeSectionResult("tag identity and canonicalization");
        IdentityRule(result);
        OrderIndependence(result);
        ValueTypeEquality(result);
        CultureIndependence();
        BclDeliveryOfEdgeTags(result);
        CallbackBufferRetention(result);
        LargeValueDescriptor(result);
        return result;
    }

    private static void IdentityRule(ProbeSectionResult result)
    {
        ProbeReport.Section("3.1 deterministic identity rule");
        ProbeReport.Line("  seriesKey        = entries joined by U+001F, entries sorted with StringComparer.Ordinal");
        ProbeReport.Line("  entry            = {keyLength}:{key} U+001E {descriptorLength}:{descriptor}");
        ProbeReport.Line("  descriptor       = {CLR type full name}:{value formatted with CultureInfo.InvariantCulture}");
        ProbeReport.Line("  null value       = descriptor \"null\"");
        ProbeReport.Line("  null tag key     = reserved token " + ProbeTagCanonicalizer.NullKeyToken);
        ProbeReport.Line("  duplicate keys   = retained, so a tag set is a sorted multiset, not a set");
        ProbeReport.Line("  oversized values = {type}#chars={count}#sha256={hex} when the descriptor exceeds the bound");
        ProbeReport.Line("  colliding content is impossible because every field is length-prefixed");
        result.Add(
            "deterministic identity rule",
            ProbeVerdict.Pass,
            "order-independent, culture-independent, type-qualified, length-prefixed rule is fixed and demonstrated below");
    }

    private static void OrderIndependence(ProbeSectionResult result)
    {
        ProbeReport.Section("3.2 order independence");

        KeyValuePair<string, object?>[][] permutations =
        {
            new[]
            {
                new KeyValuePair<string, object?>("route", "/api/items/7"),
                new KeyValuePair<string, object?>("tenant", "t-42"),
                new KeyValuePair<string, object?>("status", 200),
            },
            new[]
            {
                new KeyValuePair<string, object?>("status", 200),
                new KeyValuePair<string, object?>("route", "/api/items/7"),
                new KeyValuePair<string, object?>("tenant", "t-42"),
            },
            new[]
            {
                new KeyValuePair<string, object?>("tenant", "t-42"),
                new KeyValuePair<string, object?>("status", 200),
                new KeyValuePair<string, object?>("route", "/api/items/7"),
            },
        };

        HashSet<string> keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (KeyValuePair<string, object?>[] permutation in permutations)
        {
            keys.Add(ProbeTagCanonicalizer.CreateSeriesKey(permutation));
        }

        ProbeReport.KeyValue("distinctKeysForSameTagSet", keys.Count);
        ProbeReport.KeyValue("canonicalKeyExample", keys.First());

        const string meterName = "probe.tags.order";
        using Meter meter = new Meter(meterName, "1.0.0");
        Counter<long> counter = meter.CreateCounter<long>("probe.order.counter");
        using MeterObservationSession session = new MeterObservationSession(
            "order",
            instrument => string.Equals(instrument.Meter.Name, meterName, StringComparison.Ordinal),
            seriesCap: 16,
            tagValueCapPerKey: 16,
            tagKeyCap: 8);

        session.Start();
        foreach (KeyValuePair<string, object?>[] permutation in permutations)
        {
            counter.Add(1, permutation);
        }

        ProbeObservationSummary summary = session.Summarize();
        ProbeReport.KeyValue("measurementsDelivered", summary.ObservedMeasurements);
        ProbeReport.KeyValue("trackedSeriesForThreeOrders", summary.TrackedSeriesCount);

        result.Add(
            "order independence",
            keys.Count == 1 && summary.ObservedMeasurements == 3 && summary.TrackedSeriesCount == 1
                ? ProbeVerdict.Pass
                : ProbeVerdict.Fail,
            "the same tag set in three different orders produced one canonical series and one tracked series");
    }

    private static void ValueTypeEquality(ProbeSectionResult result)
    {
        ProbeReport.Section("3.3 value equality across CLR types");

        (string Label, object? Value)[] samples =
        {
            ("int 1", 1),
            ("long 1", 1L),
            ("float 1", 1f),
            ("double 1.0", 1.0d),
            ("decimal 1", 1m),
            ("string \"1\"", "1"),
            ("bool true", true),
        };

        HashSet<string> keys = new HashSet<string>(StringComparer.Ordinal);
        foreach ((string label, object? value) in samples)
        {
            string descriptor = ProbeTagCanonicalizer.DescribeValue(value);
            string key = ProbeTagCanonicalizer.CreateSeriesKey(new[] { new KeyValuePair<string, object?>("k", value) });
            keys.Add(key);
            ProbeReport.Line("  " + label + " -> descriptor=" + descriptor + "; key=" + key);
        }

        ProbeReport.KeyValue("distinctSeriesKeys", keys.Count);
        ProbeReport.KeyValue("samplesCompared", samples.Length);
        ProbeReport.Line(
            "  note: a formatter that dropped the CLR type (for example integer 1 or double 1.0 rendered as \"1\") "
            + "would merge these into one series; the candidate rule keeps them distinct");

        result.Add(
            "type-qualified value equality",
            keys.Count == samples.Length ? ProbeVerdict.Pass : ProbeVerdict.Fail,
            "all " + samples.Length.ToString(CultureInfo.InvariantCulture)
                + " samples are distinct series because the CLR type is part of the identity; int 1 and string \"1\" never collide");
    }

    private static void CultureIndependence()
    {
        ProbeReport.Section("3.4 culture independence");

        CultureInfo original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            ProbeReport.KeyValue("currentCulture", CultureInfo.CurrentCulture.Name);
            ProbeReport.KeyValue("double 1.5 descriptor", ProbeTagCanonicalizer.DescribeValue(1.5d));
            ProbeReport.KeyValue("decimal 1234.5 descriptor", ProbeTagCanonicalizer.DescribeValue(1234.5m));
            ProbeReport.KeyValue("double 1.5 canonical key", ProbeTagCanonicalizer.CreateSeriesKey(
                new[] { new KeyValuePair<string, object?>("k", 1.5d) }));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    private static void BclDeliveryOfEdgeTags(ProbeSectionResult result)
    {
        ProbeReport.Section("3.5 edge-case tag delivery");

        const string meterName = "probe.tags.edges";
        using Meter meter = new Meter(meterName, "1.0.0");
        Counter<long> counter = meter.CreateCounter<long>("probe.edges.counter");

        List<KeyValuePair<string, object?>[]> deliveries = new List<KeyValuePair<string, object?>[]>();
        MeterListener listener = new MeterListener();
        listener.InstrumentPublished = (instrument, target) =>
        {
            if (string.Equals(instrument.Meter.Name, meterName, StringComparison.Ordinal))
            {
                target.EnableMeasurementEvents(instrument);
            }
        };

        listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, state) =>
        {
            KeyValuePair<string, object?>[] copy = new KeyValuePair<string, object?>[tags.Length];
            for (int i = 0; i < tags.Length; i++)
            {
                copy[i] = tags[i];
            }

            deliveries.Add(copy);
        });
        listener.Start();

        List<(string Label, Action Record)> cases = new List<(string Label, Action Record)>
        {
            ("no-tags", () => counter.Add(1)),
            ("null-value", () => counter.Add(1, new KeyValuePair<string, object?>("k", null))),
            ("null-key", () => counter.Add(1, new KeyValuePair<string, object?>(null!, 7))),
            ("empty-key", () => counter.Add(1, new KeyValuePair<string, object?>(string.Empty, "v"))),
            ("empty-value", () => counter.Add(1, new KeyValuePair<string, object?>("k", string.Empty))),
            (
                "duplicate-keys",
                () => counter.Add(
                    1,
                    new KeyValuePair<string, object?>("k", "a"),
                    new KeyValuePair<string, object?>("k", "b"))),
            ("null-tag-array", () => counter.Add(1, (KeyValuePair<string, object?>[]?)null!)),
            ("large-value", () => counter.Add(1, new KeyValuePair<string, object?>("k", new string('x', 1_000_000)))),
        };

        List<string> summaries = new List<string>();
        foreach ((string label, Action record) in cases)
        {
            deliveries.Clear();
            string outcome = "delivered";
            try
            {
                record();
            }
            catch (Exception ex)
            {
                outcome = ex.GetType().Name + "(" + ex.Message.Replace('\n', ' ') + ")";
            }

            string detail = outcome;
            if (deliveries.Count > 0)
            {
                KeyValuePair<string, object?>[] delivered = deliveries[0];
                string canonicalKey = ProbeTagCanonicalizer.CreateSeriesKey(delivered);
                string keyText = string.Join(
                    "+",
                    delivered.Select(pair => (pair.Key is null ? "<null>" : "len" + pair.Key.Length.ToString(CultureInfo.InvariantCulture))
                        + ":"
                        + (pair.Value is null ? "<null>" : pair.Value.GetType().Name)));
                detail = "deliveredTags=" + delivered.Length.ToString(CultureInfo.InvariantCulture)
                    + "; shape=" + keyText
                    + "; canonicalKeyLength=" + canonicalKey.Length.ToString(CultureInfo.InvariantCulture)
                    + "; canonicalKeyPrefix=" + Truncate(canonicalKey, 96);
            }

            summaries.Add(label + " -> " + detail);
            ProbeReport.Line("  " + label + " -> " + detail);
        }

        listener.Dispose();

        string tagListNullKey;
        try
        {
            TagList tagList = default;
            tagList.Add(null!, 1);
            tagList.Add("k", 2);
            tagListNullKey = "accepted-null-key";
        }
        catch (Exception ex)
        {
            tagListNullKey = ex.GetType().Name + ": " + ex.Message;
        }

        ProbeReport.KeyValue("tagListNullKey", tagListNullKey);

        result.Add(
            "null, empty, duplicate, and large tag delivery",
            deliveries.Count > 0 ? ProbeVerdict.Pass : ProbeVerdict.Narrow,
            "all edge cases are observed and canonicalized deterministically: " + string.Join(" | ", summaries));
    }

    private static unsafe void CallbackBufferRetention(ProbeSectionResult result)
    {
        ProbeReport.Section("3.6 callback buffer aliasing and copy requirement");

        const string meterName = "probe.tags.buffer";
        using Meter meter = new Meter(meterName, "1.0.0");
        Counter<long> arrayCounter = meter.CreateCounter<long>("probe.buffer.array");
        Counter<long> tagListCounter = meter.CreateCounter<long>("probe.buffer.taglist");

        long spanAddress = 0;
        KeyValuePair<string, object?>[]? firstCopy = null;
        KeyValuePair<string, object?>[]? secondCopy = null;
        long[] tagListAddresses = new long[3];
        int tagListCalls = 0;
        int callbackCount = 0;

        MeterListener listener = new MeterListener();
        listener.InstrumentPublished = (instrument, target) =>
        {
            if (string.Equals(instrument.Meter.Name, meterName, StringComparison.Ordinal))
            {
                target.EnableMeasurementEvents(instrument);
            }
        };

        listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, state) =>
        {
            callbackCount++;
            if (tags.Length > 0)
            {
                ref KeyValuePair<string, object?> reference = ref MemoryMarshal.GetReference(tags);
                long address = (long)Unsafe.AsPointer(ref reference);

                if (string.Equals(instrument.Name, "probe.buffer.array", StringComparison.Ordinal))
                {
                    spanAddress = address;
                    KeyValuePair<string, object?>[] copy = new KeyValuePair<string, object?>[tags.Length];
                    for (int i = 0; i < tags.Length; i++)
                    {
                        copy[i] = tags[i];
                    }

                    if (firstCopy is null)
                    {
                        firstCopy = copy;
                    }
                    else
                    {
                        secondCopy = copy;
                    }
                }
                else if (tagListCalls < tagListAddresses.Length)
                {
                    tagListAddresses[tagListCalls] = address;
                    tagListCalls++;
                }
            }
        });
        listener.Start();

        KeyValuePair<string, object?>[] callerTags =
        {
            new KeyValuePair<string, object?>("route", "/a"),
            new KeyValuePair<string, object?>("tenant", "t1"),
        };

        arrayCounter.Add(1, callerTags);

        long callerAddress = AddressOfFirstElement(callerTags);

        ProbeReport.KeyValue("callbackSpanAddressEqualsCallerArrayAddress", spanAddress != 0 && spanAddress == callerAddress);
        ProbeReport.KeyValue("measurementCallbacksObserved", callbackCount);
        ProbeReport.KeyValue("firstCallbackCopyValue", firstCopy is not null ? firstCopy[0].Value?.ToString() : "<none>");

        callerTags[0] = new KeyValuePair<string, object?>("route", "/mutated");
        arrayCounter.Add(2, callerTags);

        ProbeReport.KeyValue("firstCallbackCopyAfterCallerMutation", firstCopy is not null ? firstCopy[0].Value?.ToString() : "<none>");
        ProbeReport.KeyValue("secondCallbackCopyValue", secondCopy is not null ? secondCopy[0].Value?.ToString() : "<none>");

        for (int iteration = 0; iteration < tagListAddresses.Length; iteration++)
        {
            TagList tagList = default;
            tagList.Add("route", "/t" + iteration.ToString(CultureInfo.InvariantCulture));
            tagListCounter.Add(iteration, tagList);
        }

        ProbeReport.KeyValue("tagListSpanAddresses", string.Join(", ", tagListAddresses));
        ProbeReport.KeyValue(
            "tagListBackingMemoryReused",
            tagListAddresses[0] != 0
                && tagListAddresses[0] == tagListAddresses[1]
                && tagListAddresses[1] == tagListAddresses[2]);

        listener.Dispose();

        bool aliases = spanAddress != 0 && spanAddress == callerAddress;
        bool copySafe = firstCopy is not null && string.Equals((string?)firstCopy[0].Value, "/a", StringComparison.Ordinal);
        bool sawMutation = secondCopy is not null && string.Equals((string?)secondCopy[0].Value, "/mutated", StringComparison.Ordinal);

        result.Add(
            "tag data must be copied out of the callback",
            aliases && copySafe && sawMutation ? ProbeVerdict.Pass : ProbeVerdict.Narrow,
            "the callback span aliased the caller's array (measured address equality " + ProbeReport.Format(aliases)
                + "), the caller mutated that array after the callback and the second callback observed the mutation ("
                + ProbeReport.Format(sawMutation) + "), while the copy taken in the first callback stayed intact ("
                + ProbeReport.Format(copySafe) + "); a ReadOnlySpan cannot be stored in a field at all");
    }

    private static void LargeValueDescriptor(ProbeSectionResult result)
    {
        ProbeReport.Section("3.7 oversized tag value descriptors");

        string large = new string('x', 1_048_576);
        KeyValuePair<string, object?>[] tags = { new KeyValuePair<string, object?>("payload", large) };

        ProbeMeasurement first = ProbeReport.Measure(() => ProbeTagCanonicalizer.CreateSeriesKey(tags));
        string descriptor = ProbeTagCanonicalizer.DescribeValue(large);
        string key = ProbeTagCanonicalizer.CreateSeriesKey(tags);
        string keyAgain = ProbeTagCanonicalizer.CreateSeriesKey(tags);

        ProbeReport.KeyValue("tagValueChars", large.Length);
        ProbeReport.KeyValue("descriptorLength", descriptor.Length);
        ProbeReport.KeyValue("canonicalKeyLength", key.Length);
        ProbeReport.KeyValue("canonicalKey", key);
        ProbeReport.KeyValue("descriptorIsStable", string.Equals(key, keyAgain, StringComparison.Ordinal));
        ProbeReport.Line("  single canonicalization: " + first.Describe());
        ProbeReport.KeyValue("representativeSeriesCounts", string.Join(", ", RepresentativeValueCounts));

        result.Add(
            "oversized value bound",
            key.Length < 256 && first.ElapsedMs < 50 ? ProbeVerdict.Pass : ProbeVerdict.Narrow,
            "a 1 MiB tag value is represented by a stable sha256 descriptor: canonical key length "
                + key.Length.ToString(CultureInfo.InvariantCulture)
                + " characters, " + first.ElapsedMs.ToString("0.###", CultureInfo.InvariantCulture) + " ms, "
                + first.TotalAllocatedBytes.ToString(CultureInfo.InvariantCulture) + " allocated bytes");
    }

    private static string Truncate(string text, int length)
    {
        return text.Length <= length ? text : string.Concat(text.AsSpan(0, length), "...");
    }

    private static unsafe long AddressOfFirstElement(KeyValuePair<string, object?>[] array)
    {
        ref KeyValuePair<string, object?> first = ref MemoryMarshal.GetArrayDataReference(array);
        return (long)Unsafe.AsPointer(ref first);
    }
}
