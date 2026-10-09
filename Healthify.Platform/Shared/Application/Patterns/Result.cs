namespace Healthify.Platform.Shared.Application.Patterns;

/// <summary>
///     Discriminated union returned by every application command service. Failure is a value, never
///     an exception: exceptions are reserved for genuinely exceptional situations, and control flow
///     that a caller is expected to handle travels through <see cref="Failure"/>.
/// </summary>
/// <typeparam name="TValue">Type carried by the success branch.</typeparam>
/// <typeparam name="TError">Bounded-context error enum carried by the failure branch.</typeparam>
public abstract record Result<TValue, TError>
{
    /// <summary>The operation completed and produced <paramref name="Value"/>.</summary>
    public sealed record Success(TValue Value) : Result<TValue, TError>;

    /// <summary>The operation did not complete; <paramref name="Error"/> says why.</summary>
    public sealed record Failure(TError Error) : Result<TValue, TError>;

    public bool IsSuccess => this is Success;
    public bool IsFailure => this is Failure;

    /// <summary>Transforms the success value, preserving the error branch untouched.</summary>
    public Result<TNext, TError> Map<TNext>(Func<TValue, TNext> onSuccess) =>
        this switch
        {
            Success s => new Result<TNext, TError>.Success(onSuccess(s.Value)),
            Failure f => new Result<TNext, TError>.Failure(f.Error),
            _ => throw new InvalidOperationException("Unknown Result type")
        };

    /// <summary>Collapses both branches into a single return type.</summary>
    public TResult Fold<TResult>(Func<TValue, TResult> onSuccess, Func<TError, TResult> onFailure) =>
        this switch
        {
            Success s => onSuccess(s.Value),
            Failure f => onFailure(f.Error),
            _ => throw new InvalidOperationException("Unknown Result type")
        };

    /// <summary>Runs a side effect on whichever branch is present.</summary>
    public void Match(Action<TValue> onSuccess, Action<TError> onFailure)
    {
        if (this is Success s)
            onSuccess(s.Value);
        else if (this is Failure f)
            onFailure(f.Error);
    }
}
