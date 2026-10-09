namespace Healthify.Platform.Iam.Domain.Model.ValueObjects;

/// <summary>Typed identity of a <c>User</c>.</summary>
public sealed record UserId
{
    public UserId(int value)
    {
        if (value <= 0) throw new ArgumentException("UserId must be a positive integer.", nameof(value));
        Value = value;
    }

    private UserId(int value, bool skipValidation)
    {
        Value = value;
    }

    public int Value { get; }

    /// <summary>Skips domain validation. Use ONLY from EF Core value converters.</summary>
    internal static UserId FromRaw(int value)
    {
        return new UserId(value, true);
    }

    public override string ToString()
    {
        return Value.ToString();
    }

    public static implicit operator int(UserId id)
    {
        return id.Value;
    }

    public static explicit operator UserId(int value)
    {
        return new UserId(value);
    }
}
