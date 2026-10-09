namespace Healthify.Platform.Iam.Domain.Model.Errors;

/// <summary>Every failure this bounded context can report. One value per business rule it enforces.</summary>
public enum IamError
{
    /// <summary>Rule: Unique Email Required (Subflow 1.1).</summary>
    EmailAlreadyTaken,

    InvalidEmail,

    /// <summary>Rule: Strong Password Required (Subflow 1.1).</summary>
    WeakPassword,

    /// <summary>Rule: Role Declared At Registration (Subflow 1.1).</summary>
    RoleNotDeclared,

    InvalidRole,

    UserNotFound,

    /// <summary>Rule: Valid Credentials Required (Subflow 1.2).</summary>
    InvalidCredentials,

    /// <summary>Rule: Lockout After Five Failed Attempts (Subflow 1.2).</summary>
    AccountLocked,

    SessionNotFound,

    /// <summary>Rule: Role Claim Discarded On Sign Out (Subflow 1.3).</summary>
    SessionAlreadyTerminated,

    /// <summary>Rule: One Shell Per Session (Subflow 1.2).</summary>
    ShellAlreadySelectedForSession,

    /// <summary>Rule: Role Change Requires Re Authentication (Subflow 1.2).</summary>
    RoleChangeRequiresReAuthentication,

    /// <summary>Rule: Role Immutable Per Session (Subflow 1.2).</summary>
    RoleImmutablePerSession,

    /// <summary>Rule: Name Required At Registration (IAM-1). Given names and family names, as human text.</summary>
    NameRequired,

    /// <summary>IAM-3. The language is not one the interface is offered in: es or en.</summary>
    InvalidPreferredLanguage,

    /// <summary>
    ///     IAM-4. The refresh token is unknown, expired, already rotated or belongs to a terminated session. One
    ///     value for all of them, so the endpoint does not say which.
    /// </summary>
    RefreshTokenInvalid,

    UnexpectedError
}
