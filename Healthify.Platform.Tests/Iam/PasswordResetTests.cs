using Cortex.Mediator;
using Healthify.Platform.Iam.Application.Internal.CommandServices;
using Healthify.Platform.Iam.Domain.Model.Aggregates;
using Healthify.Platform.Iam.Domain.Model.Commands;
using Healthify.Platform.Iam.Domain.Model.Errors;
using Healthify.Platform.Iam.Domain.Model.ValueObjects;
using Healthify.Platform.Iam.Domain.Repositories;
using Healthify.Platform.Iam.Domain.Services;
using Healthify.Platform.Iam.Infrastructure.Security;
using Healthify.Platform.Shared.Application.Patterns;
using Healthify.Platform.Shared.Domain.Repositories;
using Healthify.Platform.Tests.TestSupport;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Healthify.Platform.Tests.Iam;

/// <summary>
///     IAM-2. «¿Olvidaste tu contraseña?»: a six-digit code goes by email and sets a new password once, within
///     <c>Iam:PasswordResetCodeMinutes</c> and five attempts. Every session of the account ends.
/// </summary>
public class PasswordResetTests
{
    private const string Secret = "a-test-secret-that-is-long-enough-for-hmac-256";
    private const string NewPassword = "Nueva#2026x";

    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly IUserSessionRepository _sessions = Substitute.For<IUserSessionRepository>();
    private readonly IPasswordResetCodeRepository _codes = Substitute.For<IPasswordResetCodeRepository>();
    private readonly IHashingService _hashing = Substitute.For<IHashingService>();
    private readonly IEmailService _email = Substitute.For<IEmailService>();
    private readonly MutableTimeProvider _clock = new(new DateTimeOffset(2026, 10, 28, 12, 0, 0, TimeSpan.Zero));
    private readonly List<PasswordResetCode> _stored = [];
    private readonly List<UserSession> _openSessions = [];
    private readonly User _user;
    private string? _sentCode;

    public PasswordResetTests()
    {
        _user = Identity.Assign(new User(
            new RegisterAccountCommand("ana@correo.com", "Secreta#2026", "Patient", "Ana", "Flores"), "old-hash"),
            new UserId(7));
        _users.FindByEmailAsync(Arg.Is<Email>(e => e.Value == "ana@correo.com"), Arg.Any<CancellationToken>())
            .Returns(_user);
        _hashing.Hash(Arg.Any<Password>()).Returns("new-hash");

        _codes.AddAsync(Arg.Do<PasswordResetCode>(c => _stored.Add(c)), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        _codes.FindLatestByUserIdAsync(7, Arg.Any<CancellationToken>())
            .Returns(_ => _stored.LastOrDefault());
        _codes.RemoveByUserIdAsync(7, Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                _stored.Clear();
                return Task.CompletedTask;
            });
        _sessions.ListByUserIdAsync(7, Arg.Any<CancellationToken>()).Returns(_ => _openSessions);
        _email.SendPasswordResetCodeAsync(Arg.Any<Email>(), Arg.Do<string>(code => _sentCode = code),
            Arg.Any<TimeSpan>(), Arg.Any<PreferredLanguage>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
    }

    [Fact]
    public async Task An_unknown_or_malformed_email_is_accepted_and_nothing_is_sent()
    {
        Assert.IsType<Result<bool, IamError>.Success>(await Request("nadie@correo.com"));
        Assert.IsType<Result<bool, IamError>.Success>(await Request("no-es-un-correo"));
        Assert.IsType<Result<bool, IamError>.Success>(await Request(null));

        await _email.DidNotReceiveWithAnyArgs()
            .SendPasswordResetCodeAsync(null!, null!, default, null!, CancellationToken.None);
        Assert.Empty(_stored);
    }

    [Fact]
    public async Task A_known_email_gets_six_digits_and_only_their_hash_is_stored()
    {
        Assert.IsType<Result<bool, IamError>.Success>(await Request("Ana@Correo.com"));

        Assert.NotNull(_sentCode);
        Assert.Matches("^[0-9]{6}$", _sentCode);
        var stored = Assert.Single(_stored);
        Assert.Equal(7, stored.UserId);
        Assert.Equal(64, stored.CodeHash.Length);
        Assert.DoesNotContain(_sentCode!, stored.CodeHash);
        Assert.Equal(_clock.Now.AddMinutes(15), stored.ExpiresAt);
        await _email.Received(1).SendPasswordResetCodeAsync(_user.Email, _sentCode!, TimeSpan.FromMinutes(15),
            _user.PreferredLanguage, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Asking_again_within_the_cooldown_sends_nothing_and_after_it_replaces_the_code()
    {
        await Request("ana@correo.com");
        var first = _sentCode;

        _clock.Now = _clock.Now.AddSeconds(30);
        await Request("ana@correo.com");
        Assert.Single(_stored);
        await _email.ReceivedWithAnyArgs(1)
            .SendPasswordResetCodeAsync(null!, null!, default, null!, CancellationToken.None);

        _clock.Now = _clock.Now.AddSeconds(31);
        await Request("ana@correo.com");
        Assert.Single(_stored);
        await _email.ReceivedWithAnyArgs(2)
            .SendPasswordResetCodeAsync(null!, null!, default, null!, CancellationToken.None);

        // The first code no longer works once it is replaced (unless the digits happen to repeat).
        if (first != _sentCode)
            Assert.Equal(IamError.PasswordResetCodeInvalid, Failure(await Reset(first!)));
    }

    [Fact]
    public async Task The_right_code_sets_the_password_clears_the_lockout_and_ends_every_session()
    {
        for (var i = 0; i < 5; i++) _user.RegisterFailedSignInAttempt(_clock.Now, TimeSpan.FromMinutes(15));
        var phone = Identity.Assign(_user.StartSession(), new SessionId(30));
        var tablet = Identity.Assign(_user.StartSession(), new SessionId(31));
        tablet.Terminate();
        _openSessions.AddRange([phone, tablet]);
        await Request("ana@correo.com");

        Assert.IsType<Result<bool, IamError>.Success>(await Reset(_sentCode!));

        Assert.Equal("new-hash", _user.PasswordHash);
        Assert.Equal(0, _user.FailedSignInAttempts);
        Assert.Null(_user.LockedOutAt);
        Assert.False(phone.IsActive);
        Assert.Null(phone.RefreshTokenHash);
        Assert.NotNull(_stored.Single().UsedAt);
        _sessions.Received(1).Update(phone);
        _sessions.DidNotReceive().Update(tablet);
    }

    [Fact]
    public async Task A_code_works_only_once()
    {
        await Request("ana@correo.com");
        await Reset(_sentCode!);

        Assert.Equal(IamError.PasswordResetCodeInvalid, Failure(await Reset(_sentCode!)));
    }

    [Fact]
    public async Task Five_wrong_codes_leave_the_right_one_unusable()
    {
        await Request("ana@correo.com");
        var wrong = _sentCode == "000000" ? "111111" : "000000";

        for (var i = 0; i < PasswordResetCode.MaxFailedAttempts; i++)
            Assert.Equal(IamError.PasswordResetCodeInvalid, Failure(await Reset(wrong)));

        Assert.Equal(IamError.PasswordResetCodeInvalid, Failure(await Reset(_sentCode!)));
        Assert.Equal("old-hash", _user.PasswordHash);
    }

    [Fact]
    public async Task An_expired_code_is_refused()
    {
        await Request("ana@correo.com");

        _clock.Now = _clock.Now.AddMinutes(15);

        Assert.Equal(IamError.PasswordResetCodeInvalid, Failure(await Reset(_sentCode!)));
        Assert.Equal("old-hash", _user.PasswordHash);
    }

    [Fact]
    public async Task A_weak_password_is_refused_without_spending_an_attempt()
    {
        await Request("ana@correo.com");

        Assert.Equal(IamError.WeakPassword, Failure(await Reset(_sentCode!, "corta")));

        Assert.Equal(0, _stored.Single().FailedAttempts);
        Assert.IsType<Result<bool, IamError>.Success>(await Reset(_sentCode!));
    }

    [Fact]
    public async Task An_email_without_an_account_or_without_a_code_gets_the_same_refusal()
    {
        Assert.Equal(IamError.PasswordResetCodeInvalid, Failure(await Reset("123456", email: "nadie@correo.com")));
        Assert.Equal(IamError.PasswordResetCodeInvalid, Failure(await Reset("123456")));
        Assert.Equal(IamError.PasswordResetCodeInvalid, Failure(await Reset("")));
    }

    [Fact]
    public void The_same_digits_hash_differently_for_two_accounts()
    {
        var service = CodeService();
        var issued = service.Issue(7);

        Assert.True(service.Matches(7, issued.Code, issued.Hash));
        Assert.False(service.Matches(8, issued.Code, issued.Hash));
    }

    private Task<Result<bool, IamError>> Request(string? email)
    {
        return Service().Handle(new RequestPasswordResetCommand(email));
    }

    private Task<Result<bool, IamError>> Reset(string code, string password = NewPassword,
        string email = "ana@correo.com")
    {
        return Service().Handle(new ResetPasswordCommand(email, code, password));
    }

    private PasswordResetCodeService CodeService()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["TokenSettings:Secret"] = Secret })
            .Build();
        return new PasswordResetCodeService(configuration, _clock);
    }

    private PasswordResetCommandService Service()
    {
        return new PasswordResetCommandService(_users, _sessions, _codes, Substitute.For<IUnitOfWork>(), _hashing,
            CodeService(), _email, NullLogger<PasswordResetCommandService>.Instance, Substitute.For<IMediator>());
    }

    private static IamError Failure(Result<bool, IamError> result)
    {
        return Assert.IsType<Result<bool, IamError>.Failure>(result).Error;
    }
}
