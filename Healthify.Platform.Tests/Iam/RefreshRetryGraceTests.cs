using Cortex.Mediator;
using Healthify.Platform.Iam.Application.Internal;
using Healthify.Platform.Iam.Application.Internal.CommandServices;
using Healthify.Platform.Iam.Domain.Model.Aggregates;
using Healthify.Platform.Iam.Domain.Model.Commands;
using Healthify.Platform.Iam.Domain.Model.Errors;
using Healthify.Platform.Iam.Domain.Model.Events;
using Healthify.Platform.Iam.Domain.Model.ValueObjects;
using Healthify.Platform.Iam.Domain.Repositories;
using Healthify.Platform.Iam.Domain.Services;
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
///     IAM-4, retry fix. The token just rotated, presented again within the grace window while its successor is
///     unused, gets the same pair back and the session lives; outside the window, or once the successor was used,
///     it is reuse and the session ends. Two simultaneous refreshes of one token both get the same valid pair.
/// </summary>
public class RefreshRetryGraceTests
{
    private const string StrongPassword = "Secreta#2026";

    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly IHashingService _hashing = Substitute.For<IHashingService>();
    private readonly IMediator _mediator = Substitute.For<IMediator>();
    private readonly MutableTimeProvider _clock = new(new DateTimeOffset(2026, 10, 6, 12, 0, 0, 400, TimeSpan.Zero));
    private readonly InMemoryUserSessions _sessions = new();
    private readonly IConfiguration _configuration = new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
            { ["TokenSettings:Secret"] = "a-test-secret-that-is-long-enough-for-hmac-256" })
        .Build();

    public RefreshRetryGraceTests()
    {
        var user = Identity.Assign(new User(
            new RegisterAccountCommand("ana@correo.com", StrongPassword, "Patient", "Ana", "Flores"), "hash"),
            new UserId(7));
        _users.FindByIdAsync(7, Arg.Any<CancellationToken>()).Returns(user);
        _users.FindByEmailAsync(Arg.Any<Email>(), Arg.Any<CancellationToken>()).Returns(user);
        _hashing.Verify(StrongPassword, "hash").Returns(true);
    }

    [Fact]
    public async Task A_retry_within_the_window_gets_the_same_pair_and_the_session_lives()
    {
        var signIn = await SignIn();
        var first = Success(await Refresh(signIn.RefreshToken));
        var rowAfterFirst = _sessions.Row(30);

        _clock.Now = _clock.Now.AddSeconds(29);
        var retry = Success(await Refresh(signIn.RefreshToken));

        Assert.Equal((first.Token, first.RefreshToken, first.ExpiresAt),
            (retry.Token, retry.RefreshToken, retry.ExpiresAt));
        var row = _sessions.Row(30);
        Assert.True(row.IsActive);
        Assert.Equal(rowAfterFirst.RefreshTokenHash, row.RefreshTokenHash);
        Assert.Equal(rowAfterFirst.RefreshTokenRotatedAt, row.RefreshTokenRotatedAt);
        Assert.DoesNotContain(Fakes.Published(_mediator), e => e is RefreshTokenReuseDetected);

        // The pair is good: its refresh token rotates normally.
        Assert.True((await Refresh(retry.RefreshToken)).IsSuccess);
    }

    [Fact]
    public async Task A_retry_outside_the_window_is_reuse_and_ends_the_session()
    {
        var signIn = await SignIn();
        var first = Success(await Refresh(signIn.RefreshToken));

        _clock.Now = _clock.Now.AddSeconds(31);

        Assert.Equal(IamError.RefreshTokenInvalid, Failure(await Refresh(signIn.RefreshToken)));
        Assert.False(_sessions.Row(30).IsActive);
        Assert.Contains(Fakes.Published(_mediator), e => e is RefreshTokenReuseDetected { SessionId: 30 });
        Assert.Equal(IamError.RefreshTokenInvalid, Failure(await Refresh(first.RefreshToken)));
    }

    [Fact]
    public async Task Once_the_new_token_was_used_the_old_one_is_reuse_even_within_the_window()
    {
        var signIn = await SignIn();
        var first = Success(await Refresh(signIn.RefreshToken));
        Assert.True((await Refresh(first.RefreshToken)).IsSuccess);

        _clock.Now = _clock.Now.AddSeconds(5);

        Assert.Equal(IamError.RefreshTokenInvalid, Failure(await Refresh(signIn.RefreshToken)));
        Assert.False(_sessions.Row(30).IsActive);
        Assert.Contains(Fakes.Published(_mediator), e => e is RefreshTokenReuseDetected { SessionId: 30 });
    }

    [Fact]
    public void The_window_is_configurable()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["TokenSettings:RefreshReuseGraceSeconds"] = "5" })
            .Build();

        Assert.Equal(TimeSpan.FromSeconds(5), new RefreshTokenService(configuration, _clock).ReuseGrace());
        Assert.Equal(TimeSpan.FromSeconds(30), new RefreshTokenService(_configuration, _clock).ReuseGrace());
    }

    [Fact]
    public async Task Two_simultaneous_refreshes_both_get_the_same_valid_pair()
    {
        var signIn = await SignIn();
        Result<SignInOutcome, IamError>? winner = null;

        // B reads the session; A refreshes the same token completely; then B writes and loses the update.
        var loser = await Refresh(signIn.RefreshToken,
            async () => winner = await Refresh(signIn.RefreshToken));

        var a = Success(winner!);
        var b = Success(loser);
        Assert.Equal((a.Token, a.RefreshToken, a.ExpiresAt), (b.Token, b.RefreshToken, b.ExpiresAt));
        Assert.True(_sessions.Row(30).IsActive);
        Assert.True((await Refresh(a.RefreshToken)).IsSuccess);
    }

    [Fact]
    public async Task A_sign_out_that_races_a_refresh_still_signs_out()
    {
        var signIn = await SignIn();
        var (repository, unitOfWork) = _sessions.Request(async () =>
            Assert.True((await Refresh(signIn.RefreshToken)).IsSuccess));

        var result = await Service(repository, unitOfWork).Handle(new SignOutCommand(30, 7));

        Assert.True(result.IsSuccess);
        Assert.False(_sessions.Row(30).IsActive);
        Assert.Null(_sessions.Row(30).RefreshTokenHash);
    }

    [Fact]
    public void Replay_needs_the_successor_unused_the_window_and_a_live_session()
    {
        var session = Identity.Assign(_usersSession(), new SessionId(1));
        var now = _clock.Now;
        session.RotateRefreshToken(new string('a', 64), now.AddDays(30), now);
        session.RotateRefreshToken(new string('b', 64), now.AddDays(30), now);

        Assert.Equal(now.AddTicks(-(now.Ticks % TimeSpan.TicksPerSecond)), session.RefreshTokenRotatedAt);
        Assert.True(session.CanReplayRotation(new string('a', 64), new string('b', 64), now.AddSeconds(10),
            TimeSpan.FromSeconds(30)));
        Assert.False(session.CanReplayRotation(new string('a', 64), new string('c', 64), now.AddSeconds(10),
            TimeSpan.FromSeconds(30)));
        Assert.False(session.CanReplayRotation(new string('a', 64), new string('b', 64), now.AddSeconds(31),
            TimeSpan.FromSeconds(30)));

        session.Terminate();
        Assert.False(session.CanReplayRotation(new string('a', 64), new string('b', 64), now.AddSeconds(10),
            TimeSpan.FromSeconds(30)));
    }

    private UserSession _usersSession()
    {
        return Identity.Assign(new User(
                new RegisterAccountCommand("x@correo.com", StrongPassword, "Patient", "Ana", "Flores"), "hash"),
            new UserId(8)).StartSession();
    }

    private async Task<SignInOutcome> SignIn()
    {
        var (repository, unitOfWork) = _sessions.Request();
        return Success(await Service(repository, unitOfWork)
            .Handle(new SignInCommand("ana@correo.com", StrongPassword)));
    }

    private async Task<Result<SignInOutcome, IamError>> Refresh(string? token, Func<Task>? afterRead = null)
    {
        var (repository, unitOfWork) = _sessions.Request(afterRead);
        return await Service(repository, unitOfWork).Handle(new RefreshSessionCommand(token));
    }

    private UserSessionCommandService Service(IUserSessionRepository repository, IUnitOfWork unitOfWork)
    {
        return new UserSessionCommandService(_users, repository, unitOfWork, _hashing,
            new JwtTokenService(_configuration), new RefreshTokenService(_configuration, _clock),
            NullLogger<UserSessionCommandService>.Instance, _mediator,
            new Healthify.Platform.Iam.Infrastructure.Security.ConfiguredSignInLockoutPolicy(
                new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build()));
    }

    private static SignInOutcome Success(Result<SignInOutcome, IamError> result)
    {
        return Assert.IsType<Result<SignInOutcome, IamError>.Success>(result).Value;
    }

    private static IamError Failure(Result<SignInOutcome, IamError> result)
    {
        return Assert.IsType<Result<SignInOutcome, IamError>.Failure>(result).Error;
    }
}
