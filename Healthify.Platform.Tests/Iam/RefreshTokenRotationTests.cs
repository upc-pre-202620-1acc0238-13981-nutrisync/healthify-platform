using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
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
using Healthify.Platform.Iam.Interfaces.REST.Transform;
using Healthify.Platform.Shared.Application.Patterns;
using Healthify.Platform.Shared.Domain.Repositories;
using Healthify.Platform.Tests.TestSupport;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Healthify.Platform.Tests.Iam;

/// <summary>
///     IAM-4. Sign-in also issues a refresh token (only its hash is stored); each refresh rotates it; reusing a
///     rotated token terminates the session; sign-out deletes it; and a client that ignores it is unaffected.
/// </summary>
public class RefreshTokenRotationTests
{
    private const string Secret = "a-test-secret-that-is-long-enough-for-hmac-256";
    private const string StrongPassword = "Secreta#2026";

    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly IUserSessionRepository _sessions = Substitute.For<IUserSessionRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly IHashingService _hashing = Substitute.For<IHashingService>();
    private readonly IMediator _mediator = Substitute.For<IMediator>();
    private readonly MutableTimeProvider _clock = new(new DateTimeOffset(2026, 10, 6, 12, 0, 0, TimeSpan.Zero));
    private readonly List<UserSession> _stored = [];
    private readonly User _user;
    private readonly IConfiguration _configuration;

    public RefreshTokenRotationTests()
    {
        _configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["TokenSettings:Secret"] = Secret })
            .Build();

        _user = Identity.Assign(new User(
            new RegisterAccountCommand("ana@correo.com", StrongPassword, "Patient", "Ana", "Flores"), "hash"),
            new UserId(7));
        _users.FindByIdAsync(7, Arg.Any<CancellationToken>()).Returns(_user);
        _users.FindByEmailAsync(Arg.Any<Email>(), Arg.Any<CancellationToken>()).Returns(_user);
        _hashing.Verify(StrongPassword, "hash").Returns(true);

        _sessions.AddAsync(Arg.Do<UserSession>(s =>
                _stored.Add(Identity.Assign(s, new SessionId(_stored.Count + 30)))),
            Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        _sessions.FindByIdAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(call => _stored.FirstOrDefault(s => s.Id.Value == call.ArgAt<int>(0)));
        _sessions.FindByRefreshTokenHashAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => _stored.FirstOrDefault(s => s.RefreshTokenHash == call.ArgAt<string>(0)));
        _sessions.FindByPreviousRefreshTokenHashAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => _stored.FirstOrDefault(s => s.PreviousRefreshTokenHash == call.ArgAt<string>(0)));
    }

    [Fact]
    public async Task Sign_in_issues_a_refresh_token_and_stores_only_its_hash()
    {
        var outcome = await SignIn();

        Assert.False(string.IsNullOrWhiteSpace(outcome.RefreshToken));
        var session = Assert.Single(_stored);
        Assert.Matches("^[0-9a-f]{64}$", session.RefreshTokenHash);
        Assert.NotEqual(outcome.RefreshToken, session.RefreshTokenHash);
        Assert.Null(session.PreviousRefreshTokenHash);
        Assert.Equal(_clock.Now.AddDays(30), session.RefreshTokenExpiresAt);
        Assert.Equal(_clock.Now.AddMinutes(1440), outcome.ExpiresAt);
    }

    [Fact]
    public async Task A_client_that_ignores_the_refresh_token_signs_in_exactly_as_before()
    {
        var resource = SignInResponseResourceAssembler.ToResource(await SignIn());

        Assert.Equal(7, resource.UserId);
        Assert.Equal("Patient", resource.Role);
        Assert.Equal(30, resource.SessionId);
        Assert.False(string.IsNullOrWhiteSpace(resource.Token));
        Assert.NotNull(resource.RefreshToken);
        Assert.NotNull(resource.ExpiresAt);
    }

    [Fact]
    public async Task A_refresh_rotates_the_token_and_keeps_the_session_and_its_role()
    {
        var signIn = await SignIn();

        var refreshed = Success(await Service().Handle(new RefreshSessionCommand(signIn.RefreshToken)));

        Assert.NotEqual(signIn.RefreshToken, refreshed.RefreshToken);
        Assert.NotEqual(signIn.Token, refreshed.Token);
        var session = Assert.Single(_stored);
        Assert.True(session.IsActive);
        Assert.Equal(new RefreshTokenService(_configuration, _clock).Hash(signIn.RefreshToken!),
            session.PreviousRefreshTokenHash);

        var token = new JwtSecurityTokenHandler().ReadJwtToken(refreshed.Token);
        Assert.Equal("30", token.Claims.Single(c => c.Type == "sessionId").Value);
        Assert.Equal("Patient", token.Claims.Single(c => c.Type == ClaimTypes.Role).Value);

        var response = TokenRefreshResponseResourceAssembler.ToResource(refreshed);
        Assert.Equal(refreshed.RefreshToken, response.RefreshToken);
        Assert.Equal(_clock.Now.AddMinutes(1440), response.ExpiresAt);
    }

    [Fact]
    public async Task Each_new_token_is_good_for_one_use_after_another()
    {
        var current = (await SignIn()).RefreshToken;

        for (var i = 0; i < 3; i++)
            current = Success(await Service().Handle(new RefreshSessionCommand(current))).RefreshToken;

        Assert.True(Assert.Single(_stored).IsActive);
    }

    [Fact]
    public async Task Reusing_a_rotated_token_terminates_the_session_and_the_new_token_dies_with_it()
    {
        var stolen = (await SignIn()).RefreshToken;
        var legit = Success(await Service().Handle(new RefreshSessionCommand(stolen))).RefreshToken;
        _mediator.ClearReceivedCalls();
        // Past the grace window: within it, the same token would be a retry and get the same pair back.
        _clock.Now = _clock.Now.AddSeconds(31);

        var reuse = await Service().Handle(new RefreshSessionCommand(stolen));

        Assert.Equal(IamError.RefreshTokenInvalid, Failure(reuse));
        var session = Assert.Single(_stored);
        Assert.False(session.IsActive);
        Assert.Null(session.RefreshTokenHash);
        var published = Fakes.Published(_mediator);
        Assert.Contains(published, e => e is RefreshTokenReuseDetected { SessionId: 30, UserId: 7 });
        Assert.Contains(published, e => e is SessionTerminated { SessionId: 30 });

        Assert.Equal(IamError.RefreshTokenInvalid, Failure(await Service().Handle(new RefreshSessionCommand(legit))));
    }

    [Fact]
    public async Task An_expired_token_is_refused_without_ending_the_session()
    {
        var token = (await SignIn()).RefreshToken;
        _clock.Now = _clock.Now.AddDays(31);

        Assert.Equal(IamError.RefreshTokenInvalid, Failure(await Service().Handle(new RefreshSessionCommand(token))));
        Assert.True(Assert.Single(_stored).IsActive);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_missing_token_is_refused_before_anything_is_read(string? token)
    {
        Assert.Equal(IamError.RefreshTokenInvalid, Failure(await Service().Handle(new RefreshSessionCommand(token))));
        await _sessions.DidNotReceiveWithAnyArgs().FindByRefreshTokenHashAsync(default!, default);
    }

    [Fact]
    public async Task An_unknown_token_is_refused_and_nothing_is_written()
    {
        await SignIn();
        _unitOfWork.ClearReceivedCalls();

        Assert.Equal(IamError.RefreshTokenInvalid,
            Failure(await Service().Handle(new RefreshSessionCommand("not-a-token-we-issued"))));
        await _unitOfWork.DidNotReceiveWithAnyArgs().CompleteAsync(default);
        Assert.True(Assert.Single(_stored).IsActive);
    }

    [Fact]
    public async Task A_locked_account_cannot_refresh()
    {
        var token = (await SignIn()).RefreshToken;
        for (var i = 0; i < 5; i++) _user.RegisterFailedSignInAttempt(_clock.Now, TimeSpan.FromMinutes(15));

        Assert.Equal(IamError.RefreshTokenInvalid, Failure(await Service().Handle(new RefreshSessionCommand(token))));
    }

    [Fact]
    public async Task Sign_out_deletes_the_refresh_token()
    {
        var token = (await SignIn()).RefreshToken;

        Assert.True((await Service().Handle(new SignOutCommand(30, 7))).IsSuccess);

        var session = Assert.Single(_stored);
        Assert.Null(session.RefreshTokenHash);
        Assert.Null(session.RefreshTokenExpiresAt);
        Assert.Equal(IamError.RefreshTokenInvalid, Failure(await Service().Handle(new RefreshSessionCommand(token))));
    }

    [Fact]
    public void A_terminated_session_cannot_rotate()
    {
        var session = _user.StartSession();
        session.RotateRefreshToken(new string('a', 64), _clock.Now.AddDays(1));
        session.Terminate();

        Assert.Throws<InvalidOperationException>(() =>
            session.RotateRefreshToken(new string('b', 64), _clock.Now.AddDays(1)));
        Assert.False(session.AcceptsRefreshToken(new string('a', 64), _clock.Now));
    }

    [Fact]
    public void The_lifetime_is_configurable_in_days()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["TokenSettings:RefreshTokenDays"] = "7" })
            .Build();

        var issued = new RefreshTokenService(configuration, _clock).Issue();

        Assert.Equal(_clock.Now.AddDays(7), issued.ExpiresAt);
        Assert.Matches("^[0-9a-f]{64}$", issued.Hash);
    }

    private async Task<SignInOutcome> SignIn()
    {
        return Success(await Service().Handle(new SignInCommand("ana@correo.com", StrongPassword)));
    }

    private UserSessionCommandService Service()
    {
        return new UserSessionCommandService(_users, _sessions, _unitOfWork, _hashing,
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
