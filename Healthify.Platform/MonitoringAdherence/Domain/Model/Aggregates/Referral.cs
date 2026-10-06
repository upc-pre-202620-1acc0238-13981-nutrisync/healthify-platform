using Healthify.Platform.MonitoringAdherence.Domain.Model.Commands;
using Healthify.Platform.MonitoringAdherence.Domain.Model.ValueObjects;

namespace Healthify.Platform.MonitoringAdherence.Domain.Model.Aggregates;

/// <summary>
///     The practitioner sending the patient to someone else.
/// </summary>
/// <remarks>
///     Business rule: Specialty And Reason Required (Subflow 5.10). Both are constructor parameters
///     of types that refuse to exist empty, so a referral without either cannot be built. A referral
///     that does not say where to or why is a dead end for whoever receives it.
///     A referral is a record that something was decided on a date, not a workflow this platform then
///     manages: what happens at the other end happens outside.
///     RM-4 (DECISIÓN §12-#8): it carries one state of its own, Open or Closed, closed by hand by the practitioner
///     who issued it, so PAC-3 can say "en curso" and PT20 "Sin derivaciones activas". Nothing else changes it.
/// </remarks>
public partial class Referral
{
    /// <summary>RM-4. Still in course ("en curso").</summary>
    public const string Open = "Open";

    /// <summary>RM-4. Closed by the practitioner.</summary>
    public const string Closed = "Closed";

    /// <summary>Required by EF Core.</summary>
    protected Referral()
    {
    }

    /// <summary>Subflow 5.10 - Record Referral.</summary>
    /// <param name="command">Who is being referred, by whom, where to and why.</param>
    public Referral(RecordReferralCommand command)
    {
        PatientId = command.PatientId;
        IssuedBy = command.PractitionerId;

        // Business rule: Specialty And Reason Required (Monitoring and Adherence, Subflow 5.10)
        Specialty = new Specialty(command.Specialty);
        Reason = new ReferralReason(command.Reason);

        IssuedAt = DateTimeOffset.UtcNow;
        Status = Open;
    }

    public ReferralId Id { get; private set; } = null!;

    /// <summary>Cross-context reference to the patient. A plain int, no EF navigation.</summary>
    public int PatientId { get; private set; }

    public Specialty Specialty { get; private set; } = null!;

    public ReferralReason Reason { get; private set; } = null!;

    /// <summary>The practitioner who issued it. Cross-context reference, a plain int.</summary>
    public int IssuedBy { get; private set; }

    public DateTimeOffset IssuedAt { get; private set; }

    /// <summary>RM-4. Open or Closed. Referrals recorded before RM-4 are Open.</summary>
    public string Status { get; private set; } = Open;

    /// <summary>RM-4. When it was closed, or null while it is open.</summary>
    public DateTimeOffset? ClosedAt { get; private set; }

    public bool IsOpen => Status == Open;

    /// <summary>RM-4 - Close Referral. Business rule: A Referral Is Closed Once (RM-4).</summary>
    /// <exception cref="InvalidOperationException">When it is already closed.</exception>
    public void Close(DateTimeOffset at)
    {
        if (!IsOpen) throw new InvalidOperationException("This referral is already closed.");
        Status = Closed;
        ClosedAt = at;
    }
}
