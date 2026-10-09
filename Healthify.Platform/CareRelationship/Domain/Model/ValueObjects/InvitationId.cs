namespace Healthify.Platform.CareRelationship.Domain.Model.ValueObjects;

/// <summary>Typed identity of an <c>Invitation</c>.</summary>
public sealed record InvitationId
{
    public InvitationId(int value)
    {
        if (value <= 0) throw new ArgumentException("InvitationId must be a positive integer.", nameof(value));
        Value = value;
    }

    private InvitationId(int value, bool skipValidation)
    {
        Value = value;
    }

    public int Value { get; }

    /// <summary>Skips domain validation. Use ONLY from EF Core value converters.</summary>
    internal static InvitationId FromRaw(int value)
    {
        return new InvitationId(value, true);
    }

    public override string ToString()
    {
        return Value.ToString();
    }

    public static implicit operator int(InvitationId id)
    {
        return id.Value;
    }

    public static explicit operator InvitationId(int value)
    {
        return new InvitationId(value);
    }
}
