using Healthify.Platform.IntakeBodyResponse.Domain.Model.ValueObjects;

namespace Healthify.Platform.IntakeBodyResponse.Domain.Model.Aggregates;

/// <summary>
///     One reading the patient took of themselves.
/// </summary>
/// <remarks>
///     Business rules: Plausible Weight Range and Protocol Compliance Declared (Subflow 4.5).
///     A self weigh-in is not a clinical measurement and this context never pretends otherwise. It is
///     taken without supervision, on an unknown scale, at an hour nobody recorded, which is exactly
///     why the protocol is declared alongside it rather than assumed.
///     Business rule: Excluded Weigh Ins Are Kept As Data (Subflow 4.5). A reading taken outside the
///     protocol is stored in full. It does not smooth the trend, and that is the whole of the
///     consequence: nothing is discarded, nothing is flagged, and nobody is told they did it wrong.
/// </remarks>
public partial class SelfWeighIn
{
    /// <summary>Required by EF Core.</summary>
    protected SelfWeighIn()
    {
    }

    /// <param name="patientId">Who weighed themselves.</param>
    /// <param name="valueKg">The reading.</param>
    /// <param name="localTimestamp">The moment the patient declared.</param>
    /// <param name="protocolCompliance">What the patient declared about the protocol.</param>
    /// <param name="clientEntryId">IN-4. The identifier the device generated offline; null when recorded online.</param>
    public SelfWeighIn(int patientId, WeightKg valueKg, LocalTimestamp localTimestamp,
        ProtocolCompliance protocolCompliance, Guid? clientEntryId = null)
    {
        if (clientEntryId == Guid.Empty)
            throw new ArgumentException("A queued reading must carry the identifier its device gave it.",
                nameof(clientEntryId));

        PatientId = patientId;
        ClientEntryId = clientEntryId;
        ValueKg = valueKg.Value;
        LocalTimestamp = localTimestamp.Value.DateTime;
        LocalUtcOffsetMinutes = (int)localTimestamp.Value.Offset.TotalMinutes;

        ProtocolFastedState = protocolCompliance.FastedState;
        ProtocolSameTimeOfDay = protocolCompliance.SameTimeOfDay;
        ProtocolSameScale = protocolCompliance.SameScale;
    }

    public SelfWeighInId Id { get; private set; } = null!;

    /// <summary>Cross-context reference to the patient account. A plain int, no EF navigation.</summary>
    public int PatientId { get; private set; }

    public decimal ValueKg { get; private set; }

    /// <summary>
    ///     IN-4. The identifier the device generated while offline. Business rule: Idempotency By Aggregate
    ///     Id (Subflow 4.6), unique per patient. Null for readings recorded online.
    /// </summary>
    public Guid? ClientEntryId { get; private set; }

    /// <summary>
    ///     The wall clock the patient was reading when they weighed themselves.
    /// </summary>
    /// <remarks>
    ///     Stored the same way as on a diary entry, and for the same reason: the day a reading
    ///     belongs to is the day the patient was living, not the day it was in UTC. Same time of day
    ///     was one of the protocol conditions before IN-3, and could be again by configuration, so
    ///     losing the local hour would make the protocol unverifiable after the fact.
    /// </remarks>
    public DateTime LocalTimestamp { get; private set; }

    /// <summary>
    ///     NOTE: technical field, not part of the domain model. The offset the device declared,
    ///     stored beside the wall clock so that the moment can be rebuilt exactly as it was given.
    /// </summary>
    public int LocalUtcOffsetMinutes { get; private set; }

    /// <summary>The moment the patient declared, rebuilt whole.</summary>
    public DateTimeOffset DeclaredLocalTimestamp =>
        new(LocalTimestamp, TimeSpan.FromMinutes(LocalUtcOffsetMinutes));

    // Persisted projection of the ProtocolCompliance value object.
    public bool ProtocolFastedState { get; private set; }

    /// <summary>IN-3: no longer asked; null on readings recorded after IN-3.</summary>
    public bool? ProtocolSameTimeOfDay { get; private set; }

    /// <summary>IN-3: no longer asked; null on readings recorded after IN-3.</summary>
    public bool? ProtocolSameScale { get; private set; }

    /// <summary>Rebuilt from the stored columns.</summary>
    public ProtocolCompliance ProtocolCompliance =>
        new(ProtocolFastedState, ProtocolSameTimeOfDay, ProtocolSameScale);

    /// <summary>
    ///     Business rule: Only Protocol Compliant Weigh Ins Smooth The Trend (Subflow 4.5), under the default
    ///     protocol (IN-3: fasted).
    /// </summary>
    public bool FollowsProtocol => ProtocolCompliance.FollowsProtocol;

    /// <summary>IN-3. Whether this reading smooths the trend under the configured protocol.</summary>
    public bool FollowsProtocolUnder(SelfWeighInProtocol protocol)
    {
        return ProtocolCompliance.FollowsUnder(protocol);
    }

    /// <summary>The calendar day the patient was living when they weighed themselves.</summary>
    public DateOnly LocalDate => DateOnly.FromDateTime(LocalTimestamp);
}
