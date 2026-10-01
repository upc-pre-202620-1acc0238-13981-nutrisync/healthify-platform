using Healthify.Platform.CareRelationship.Domain.Model.Commands;
using Healthify.Platform.CareRelationship.Domain.Model.ValueObjects;

namespace Healthify.Platform.CareRelationship.Domain.Model.Aggregates;

/// <summary>
///     The single-use token a practitioner shows as a QR code during the consultation. It is the only
///     way into the platform for a patient: registering an account grants access to nothing, and a
///     patient can never create their own care link.
/// </summary>
public partial class Invitation
{
    /// <summary>Required by EF Core.</summary>
    protected Invitation()
    {
    }

    public Invitation(IssueInvitationCommand command)
    {
        // Business rule: Expiration Date Required (Care Relationship, Subflow 2.1)
        if (command.ExpiresAt == default)
            throw new ArgumentException("An expiration date is required.", nameof(command));
        if (command.ExpiresAt <= DateTimeOffset.UtcNow)
            throw new ArgumentException("The expiration date must be in the future.", nameof(command));
        if (command.IssuedBy <= 0)
            throw new ArgumentException("An invitation must record who issued it.", nameof(command));

        IssuedBy = command.IssuedBy;
        // Business rule: Single Use Token (Care Relationship, Subflow 2.1)
        Token = InvitationToken.Generate();
        ExpiresAt = command.ExpiresAt;
        RedeemedAt = null;
        ExpiredAt = null;
    }

    public InvitationId Id { get; private set; } = null!;

    /// <summary>Identifier of the issuing practitioner. Cross-context reference: a plain int.</summary>
    public int IssuedBy { get; private set; }

    public InvitationToken Token { get; private set; } = null!;
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? RedeemedAt { get; private set; }

    /// <summary>
    ///     Set when the expiry policy runs. Distinct from <see cref="ExpiresAt" />, which is only the
    ///     date the invitation was meant to die on.
    /// </summary>
    public DateTimeOffset? ExpiredAt { get; private set; }

    /// <summary>Business rule: Single Use Token (Subflow 2.1).</summary>
    public bool IsRedeemed => RedeemedAt is not null;

    public bool IsExpired => ExpiredAt is not null;

    /// <summary>Business rule: Invitation Must Be Valid (Subflow 2.2).</summary>
    public bool IsValidAt(DateTimeOffset moment)
    {
        return !IsRedeemed && !IsExpired && ExpiresAt > moment;
    }

    /// <summary>Subflow 2.2 - Redeem Invitation.</summary>
    public void Redeem(DateTimeOffset redeemedAt)
    {
        // Business rule: Invitation Must Be Unused (Care Relationship, Subflow 2.2)
        if (IsRedeemed) throw new InvalidOperationException("This invitation has already been redeemed.");

        // Business rule: Invitation Must Be Valid (Care Relationship, Subflow 2.2)
        if (IsExpired || ExpiresAt <= redeemedAt)
            throw new ArgumentException("This invitation is no longer valid.", nameof(redeemedAt));

        RedeemedAt = redeemedAt;
    }

    /// <summary>Subflow 2.1 - Expire Invitation. Reached only from the time-driven policy.</summary>
    public void Expire(DateTimeOffset expiredAt)
    {
        // Business rule: Redeemed Invitation Cannot Expire (Care Relationship, Subflow 2.1)
        // An invitation that was used has already done its job; expiring it afterwards would
        // rewrite history.
        if (IsRedeemed) throw new InvalidOperationException("A redeemed invitation cannot expire.");

        // Re-running the policy over an already expired invitation is a no-op, so the cycle stays
        // idempotent.
        if (IsExpired) return;

        ExpiredAt = expiredAt;
    }
}
