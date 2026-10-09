using Cortex.Mediator;
using Healthify.Platform.Iam.Application.Internal;
using Healthify.Platform.Iam.Application.Internal.CommandServices;
using Healthify.Platform.Iam.Domain.Model.Aggregates;
using Healthify.Platform.Iam.Domain.Model.Commands;
using Healthify.Platform.Iam.Domain.Model.Errors;
using Healthify.Platform.Iam.Domain.Model.ValueObjects;
using Healthify.Platform.Iam.Domain.Repositories;
using Healthify.Platform.Iam.Domain.Services;
using Healthify.Platform.Iam.Infrastructure.Security;
using Healthify.Platform.Iam.Infrastructure.Tokens.JWT;
using Healthify.Platform.Iam.Infrastructure.Tokens.Refresh;
using Healthify.Platform.Shared.Application.Patterns;
using Healthify.Platform.Shared.Domain.Repositories;
using Healthify.Platform.Tests.TestSupport;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Healthify.Platform.Tests.Iam;

/// <summary>
///     IAM-5. «Tu cuenta está bloqueada temporalmente»: five failures lock the account for <c>Iam:LockoutMinutes</c>
///     (15 by default); once that time has passed, the right password signs in again.
/// </summary>
public class TemporaryLockoutTests
{
    private const string Secret = "a-test-secret-that-is-long-enough-for-hmac-256";
    private const string StrongPassword = "Secreta#2026";
    private const string WrongPassword = "Otra#2026x";

    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly IUserSessionRepository _sessions = Substitute.For<IUserSessionRepository>();
    private readonly IHashingService _hashing = Substitute.For<IHashingService>();
    private readonly MutableTimeProvider _clock = new(new DateTimeOffset(2026, 10, 6, 12, 0, 0, TimeSpan.Zero));
    private readonly User _user;

    public TemporaryLockoutTests()
    {
        _user = Identity.Assign(new User(
            new RegisterAccountCommand("ana@correo.com", StrongPassword, "Patient", "Ana", "Flores"), "hash"),
            new UserId(7));
        _users.FindByEmailAsync(Arg.Any<Email>(), Arg.Any<CancellationToken>()).Returns(_user);
        _hashing.Verify(StrongPassword, "hash").Returns(true);
        _hashing.Verify(WrongPassword, "hash").Returns(false);
        _sessions.AddAsync(Arg.Do<UserSession>(s => Identity.Assign(s, new SessionId(30))),
            Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
    }

    [Fact]
    public void Lockout_applies_only_while_its_duration_has_not_elapsed()
    {
        var at = _clock.Now;
        for (var i = 0; i < 5; i++) _user.RegisterFailedSignInAttempt(at, TimeSpan.FromMinutes(15));

        Assert.True(_user.IsLockedOutAt(at.AddMinutes(14).AddSeconds(59), TimeSpan.FromMinutes(15)));
        Assert.False(_user.IsLockedOutAt(at.AddMinutes(15), TimeSpan.FromMinutes(15)));
    }

    [Fact]
    public void A_failure_after_an_expired_lockout_starts_a_new_count()
    {
        var at = _clock.Now;
        var duration = TimeSpan.FromMinutes(15);
        for (var i = 0; i < 5; i++) _user.RegisterFailedSignInAttempt(at, duration);

        _user.RegisterFailedSignInAttempt(at.AddMinutes(16), duration);

        Assert.Equal(1, _user.FailedSignInAttempts);
        Assert.False(_user.IsLockedOutAt(at.AddMinutes(16), duration));
    }

    [Fact]
    public async Task Right_password_is_refused_during_the_lockout_and_accepted_after_it()
    {
        for (var i = 0; i < 4; i++)
            Assert.Equal(IamError.InvalidCredentials, Failure(await SignIn(WrongPassword)));
        Assert.Equal(IamError.AccountLocked, Failure(await SignIn(WrongPassword)));

        _clock.Now = _clock.Now.AddMinutes(14);
        Assert.Equal(IamError.AccountLocked, Failure(await SignIn(StrongPassword)));

        _clock.Now = _clock.Now.AddMinutes(1);
        Assert.IsType<Result<SignInOutcome, IamError>.Success>(await SignIn(StrongPassword));
        Assert.Equal(0, _user.FailedSignInAttempts);
        Assert.Null(_user.LockedOutAt);
    }

    [Fact]
    public async Task Lockout_minutes_are_read_from_configuration()
    {
        for (var i = 0; i < 5; i++) await SignIn(WrongPassword, lockoutMinutes: 2);

        _clock.Now = _clock.Now.AddMinutes(1);
        Assert.Equal(IamError.AccountLocked, Failure(await SignIn(StrongPassword, lockoutMinutes: 2)));

        _clock.Now = _clock.Now.AddMinutes(1);
        Assert.IsType<Result<SignInOutcome, IamError>.Success>(await SignIn(StrongPassword, lockoutMinutes: 2));
    }

    [Fact]
    public void Default_lockout_is_fifteen_minutes()
    {
        Assert.Equal(TimeSpan.FromMinutes(15),
            new ConfiguredSignInLockoutPolicy(new ConfigurationBuilder().Build()).LockoutDuration());
    }

    private Task<Result<SignInOutcome, IamError>> SignIn(string password, int? lockoutMinutes = null)
    {
        var settings = new Dictionary<string, string?> { ["TokenSettings:Secret"] = Secret };
        if (lockoutMinutes is not null) settings["Iam:LockoutMinutes"] = lockoutMinutes.ToString();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        var service = new UserSessionCommandService(_users, _sessions, Substitute.For<IUnitOfWork>(), _hashing,
            new JwtTokenService(configuration), new RefreshTokenService(configuration, _clock),
            NullLogger<UserSessionCommandService>.Instance, Substitute.For<IMediator>(),
            new ConfiguredSignInLockoutPolicy(configuration));
        return service.Handle(new SignInCommand("ana@correo.com", password));
    }

    private static IamError Failure(Result<SignInOutcome, IamError> result)
    {
        return Assert.IsType<Result<SignInOutcome, IamError>.Failure>(result).Error;
    }
}
