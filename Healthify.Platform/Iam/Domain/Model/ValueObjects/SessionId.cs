namespace Healthify.Platform.Iam.Domain.Model.ValueObjects;

/// <summary>Typed identity of a <c>UserSession</c>.</summary>
public sealed record SessionId
{
    public SessionId(int value)
    {
        if (value <= 0) throw new ArgumentException("SessionId must be a positive integer.", nameof(value));
        Value = value;
    }

    private SessionId(int value, bool skipValidation)
    {
        Value = value;
    }

    public int Value { get; }

    /// <summary>Skips domain validation. Use ONLY from EF Core value converters.</summary>
    internal static SessionId FromRaw(int value)
    {
        return new SessionId(value, true);
    }

    public override string ToString()
    {
        return Value.ToString();
    }

    public static implicit operator int(SessionId id)
    {
        return id.Value;
    }

    public static explicit operator SessionId(int value)
    {
        return new SessionId(value);
    }
}
