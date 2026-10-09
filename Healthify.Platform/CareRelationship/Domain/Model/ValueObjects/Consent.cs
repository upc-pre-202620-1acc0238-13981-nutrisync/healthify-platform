namespace Healthify.Platform.CareRelationship.Domain.Model.ValueObjects;

/// <summary>
///     The permission the patient gives, and can take back at any time without explaining why.
///     Nothing in any bounded context is readable without an active care link and a live consent.
/// </summary>
/// <remarks>
///     Enforces the business rule "Consent Scope Recorded" (Subflow 2.3): a consent without a
///     recorded scope is not a consent, it is an assumption.
///     CR-2 adds the consent to AI processing, given separately inside the same consent (PT2 "INTELIGENCIA
///     ARTIFICIAL"): processing the patient's data with an AI provider is a different permission from sharing it
///     with the practitioner, and saying no to it changes nothing else.
/// </remarks>
public sealed record Consent
{
    private const int MaximumScopeLength = 200;

    public Consent(bool isGranted, string scope, DateTimeOffset grantedAt, DateTimeOffset? withdrawnAt = null,
        bool aiProcessingGranted = false, DateTimeOffset? aiProcessingDecidedAt = null)
    {
        // Business rule: Consent Scope Recorded (Care Relationship, Subflow 2.3)
        if (string.IsNullOrWhiteSpace(scope))
            throw new ArgumentException("The consent scope must be recorded.", nameof(scope));
        if (scope.Length > MaximumScopeLength)
            throw new ArgumentException(
                $"The consent scope exceeds the maximum length of {MaximumScopeLength}.", nameof(scope));
        if (withdrawnAt is not null && withdrawnAt < grantedAt)
            throw new ArgumentException("Consent cannot be withdrawn before it was granted.", nameof(withdrawnAt));

        // Business rule: AI Processing Needs Live Consent (CR-2). AI consent lives inside the consent: without a
        // live consent there is nothing to process, and withdrawing consent also withdraws it.
        if (aiProcessingGranted && !isGranted)
            throw new ArgumentException("AI processing cannot be granted without a live consent.",
                nameof(aiProcessingGranted));
        if (aiProcessingGranted && aiProcessingDecidedAt is null)
            throw new ArgumentException("A granted AI processing consent records when it was decided.",
                nameof(aiProcessingDecidedAt));
        if (aiProcessingDecidedAt is not null && aiProcessingDecidedAt < grantedAt)
            throw new ArgumentException("AI processing cannot be decided before consent was granted.",
                nameof(aiProcessingDecidedAt));

        IsGranted = isGranted;
        Scope = scope.Trim();
        GrantedAt = grantedAt;
        WithdrawnAt = withdrawnAt;
        AiProcessingGranted = aiProcessingGranted;
        AiProcessingDecidedAt = aiProcessingDecidedAt;
    }

    public bool IsGranted { get; }
    public string Scope { get; }
    public DateTimeOffset GrantedAt { get; }
    public DateTimeOffset? WithdrawnAt { get; }

    /// <summary>CR-2. Whether the patient allows their data to be processed by the AI functions.</summary>
    public bool AiProcessingGranted { get; }

    /// <summary>
    ///     CR-2. When the patient last turned AI processing on or off. Null while they never decided: AI is off by
    ///     default, and a consent given without touching the switch is not a decision about AI.
    /// </summary>
    public DateTimeOffset? AiProcessingDecidedAt { get; }

    /// <summary>
    ///     Business rule: Consent Always Revocable (Subflow 2.3). Withdrawing consent also turns AI processing off
    ///     (CR-2); the moment is recorded as the AI decision only if it was on.
    /// </summary>
    public Consent Withdraw(DateTimeOffset withdrawnAt)
    {
        return new Consent(false, Scope, GrantedAt, withdrawnAt, false,
            AiProcessingGranted ? withdrawnAt : AiProcessingDecidedAt);
    }

    /// <summary>CR-2. The same consent with AI processing turned on or off at <paramref name="decidedAt" />.</summary>
    /// <exception cref="ArgumentException">Turning it on without a live consent.</exception>
    public Consent WithAiProcessing(bool granted, DateTimeOffset decidedAt)
    {
        return new Consent(IsGranted, Scope, GrantedAt, WithdrawnAt, granted, decidedAt);
    }
}
