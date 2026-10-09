namespace Healthify.Platform.Tests.TestSupport;

/// <summary>
///     Assigns the typed identity that EF Core would generate on insert. Aggregates expose
///     <c>Id</c> with a private setter, and command services read <c>Id.Value</c> right after the
///     commit to build their events, so unit tests without a database have to stand in for it.
/// </summary>
public static class Identity
{
    public static T Assign<T>(T aggregate, object id) where T : class
    {
        var property = typeof(T).GetProperty("Id")
                       ?? throw new InvalidOperationException($"{typeof(T).Name} has no Id property.");
        property.SetValue(aggregate, id);
        return aggregate;
    }
}
