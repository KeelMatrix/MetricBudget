// Copyright (c) KeelMatrix

namespace KeelMatrix.MetricBudget;

/// <summary>
/// Identifies the static metadata dimension that prevented an instrument identity from being admitted.
/// </summary>
public enum MetricBudgetStaticMetadataFailureKind
{
    /// <summary>A unit or description exceeded the effective static metadata text bound.</summary>
    TextLength = 0,

    /// <summary>The instrument measurement type could not be represented by the identity contract.</summary>
    MeasurementType = 1,

    /// <summary>The meter tag collection exceeded its effective static metadata tag-count bound.</summary>
    MeterTagCount = 2,

    /// <summary>A meter tag key exceeded its effective static metadata key-length bound.</summary>
    MeterTagKeyLength = 3,

    /// <summary>A meter tag value exceeded its effective static metadata value-length bound.</summary>
    MeterTagValueLength = 4,

    /// <summary>A meter tag value used an unsupported type.</summary>
    MeterTagValue = 5,

    /// <summary>The meter tag collection could not be enumerated safely.</summary>
    MeterTagEnumeration = 6,

    /// <summary>The instrument tag collection exceeded its effective static metadata tag-count bound.</summary>
    InstrumentTagCount = 7,

    /// <summary>An instrument tag key exceeded its effective static metadata key-length bound.</summary>
    InstrumentTagKeyLength = 8,

    /// <summary>An instrument tag value exceeded its effective static metadata value-length bound.</summary>
    InstrumentTagValueLength = 9,

    /// <summary>An instrument tag value used an unsupported type.</summary>
    InstrumentTagValue = 10,

    /// <summary>The instrument tag collection could not be enumerated safely.</summary>
    InstrumentTagEnumeration = 11,
}

/// <summary>
/// One static-metadata admission failure observed while selecting instruments.
/// </summary>
/// <remarks>
/// Static metadata is bounded separately from delivered measurement tags. A failure means the affected identity was
/// not retained or enabled, so the report is incomplete. Values are never included in this record.
/// </remarks>
public sealed class MetricBudgetStaticMetadataFailure
{
    internal MetricBudgetStaticMetadataFailure(
        MetricBudgetStaticMetadataFailureKind kind,
        long rejectedInstrumentCount,
        int? effectiveLimit)
    {
        Kind = kind;
        RejectedInstrumentCount = rejectedInstrumentCount;
        EffectiveLimit = effectiveLimit;
    }

    /// <summary>The static metadata dimension that caused the rejection.</summary>
    public MetricBudgetStaticMetadataFailureKind Kind { get; }

    /// <summary>Number of selected instrument identities rejected for this dimension.</summary>
    public long RejectedInstrumentCount { get; }

    /// <summary>
    /// Effective bound when the failure is a length or count failure, including the fixed hard ceiling; otherwise
    /// <see langword="null"/> for unsupported values or enumeration failures.
    /// </summary>
    public int? EffectiveLimit { get; }
}
