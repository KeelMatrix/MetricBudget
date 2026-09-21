# Series identity

An observed series is the combination of one instrument identity and one tag set. Identity must be deterministic
and order-independent, otherwise the same combination would be counted repeatedly and the observed cardinality
would be wrong.

## Rule

```text
seriesKey  = instrumentKey U+001F tagSetKey

instrumentKey = meterNameField U+001F meterVersionField U+001F instrumentNameField U+001F kindField
meterNameField      = {length}:{meter name}
meterVersionField   = {length}:{meter version}, or the bare marker null when the meter has no version
instrumentNameField = {length}:{instrument name}
kindField           = {length}:{counter|updowncounter|histogram|observablecounter|observableupdowncounter|observablegauge}

tagSetKey = entries joined by U+001F, entries sorted with ordinal string comparison
entry     = keyField U+001E valueField
keyField  = {length}:{delivered tag key}, or the bare marker null when the delivered key is null
valueField = {length}:{descriptor}
descriptor = one of the supported, lossless forms below
null value descriptor = null
oversized descriptor  = {CLR type full name}#chars={count}#sha256={hex}
```

Supported tag values are `null`, `string`, `bool`, all eight integral types, `float`, `double`, `decimal`,
`char`, `DateTime`, `DateTimeOffset`, `TimeSpan`, and `Guid`. Integral values use invariant decimal text;
floating-point values use their exact IEEE bit patterns; `decimal` uses all four values returned by
`decimal.GetBits`; date/time values use their exact ticks, offset, or `DateTime.ToBinary()` representation; and
GUIDs use their canonical hexadecimal form. Strings are sequences of UTF-16 code units, including unpaired
surrogates. Oversized strings are hashed from those exact code units, without a replacement fallback.

Other objects are unsupported. The session does not call `ToString()` or any user code to define identity; it marks
the delivered measurement incomplete and non-passing instead. A tag key longer than `MaxTagKeyLength`, or a tag set
with more than `MaxTagCount` entries, is handled the same way. These rules apply to malformed strings in tag keys as
well as values.

Notes that the implementation, the diagnostics, and this document share:

- **Order independence.** Entries are sorted ordinally and joined, so the tag order a caller used never changes
  identity.
- **Duplicates are retained.** A tag set is a sorted multiset: the same key delivered twice with the same value
  produces a different identity from the same key delivered once.
- **The bare `null` marker is unambiguous.** Every length-prefixed field starts with an ASCII digit, so no
  delivered key or name can produce the bare `null` marker. A null tag key, the literal key `null`, and the empty
  key are three different identities.
- **The CLR type is part of identity.** `int 1`, `double 1`, and `string "1"` are different identities, so a
  type-blind formatter cannot merge them.
- **Oversized values are digested.** When a value's invariant text is longer than `MaxTagValueLength`, the
  descriptor becomes a stable hash of the exact supported representation, so one pathological value cannot inflate
  an identity while different values stay distinct.
- **Retained accounting uses digests.** The session stores only a fixed-size SHA-256 digest of each series key and
  tag-value descriptor in its bounded accounting state. Canonical strings and reusable UTF-16 byte scratch can
  briefly exist during hashing; the scratch is cleared after each digest, but ordinary managed process memory is
  not a secure-erasure boundary. Raw tag values never appear in reports or telemetry.
- **Instrument identity is part of the series key.** Two instruments with the same name in different meters, or in
  different versions of one meter name, never share accounting. Instrument *kind* is part of identity as well, so
  a counter and a histogram with the same name are separate. `Meter.Scope` is not part of identity, matching the
  instrumentation-scope model the BCL metrics APIs and the OpenTelemetry .NET SDK use, so two `Meter` instances
  that share a name and version share one observed identity.
- **Selected identity admission is bounded.** Selector matching happens before the identity component-length bound,
  so oversized unselected instruments do not affect a scoped session. An oversized selected identity is rejected and
  makes the report incomplete; a selected identity or physical instance rejected by an admission bound also makes
  any affected retained result incomplete. Focused assertions do not draw a pass conclusion from those results.

The rule is implemented by `TagIdentity` in the library. If the code and this document ever disagree, the code is
wrong: the product documentation, the printed diagnostics, and the implementation are required to agree.
