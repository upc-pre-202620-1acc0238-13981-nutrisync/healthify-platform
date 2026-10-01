using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Cortex.Mediator;
using Healthify.Platform.Iam.Application.Acl;
using Healthify.Platform.Iam.Application.Internal.CommandServices;
using Healthify.Platform.Iam.Application.QueryServices;
using Healthify.Platform.Iam.Domain.Model.Aggregates;
using Healthify.Platform.Iam.Domain.Model.Commands;
using Healthify.Platform.Iam.Domain.Model.Errors;
using Healthify.Platform.Iam.Domain.Model.Queries;
using Healthify.Platform.Iam.Domain.Model.ValueObjects;
using Healthify.Platform.Iam.Domain.Repositories;
using Healthify.Platform.Iam.Domain.Services;
using Healthify.Platform.Iam.Infrastructure.Localization;
using Healthify.Platform.Iam.Infrastructure.Tokens.JWT;
using Healthify.Platform.Iam.Interfaces.REST.Transform;
using Healthify.Platform.Iam.Resources;
using Healthify.Platform.Shared.Application.Patterns;
using Healthify.Platform.Shared.Domain.Repositories;
using Healthify.Platform.Tests.TestSupport;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Healthify.Platform.Tests.Iam;

/// <summary>
///     IAM-3. The account keeps its interface language (es by default), the owner changes it, the next sign-in
///     returns it and carries it as the lang claim, and the errors come out in it when the client sends no
///     Accept-Language.
/// </summary>
public class PreferredLanguageTests
{
    private const string Secret = "a-test-secret-that-is-long-enough-for-hmac-256";
    private const string StrongPassword = "Secreta#2026";

    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly IUserSessionRepository _sessions = Substitute.For<IUserSessionRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly IHashingService _hashing = Substitute.For<IHashingService>();
    private readonly IMediator _mediator = Substitute.For<IMediator>();
    private readonly User _user;

    public PreferredLanguageTests()
    {
        _user = Identity.Assign(new User(
            new RegisterAccountCommand("ana@correo.com", StrongPassword, "Patient", "Ana", "Flores"), "hash"),
            new UserId(7));
        _users.FindByIdAsync(7, Arg.Any<CancellationToken>()).Returns(_user);
        _users.FindByEmailAsync(Arg.Any<Email>(), Arg.Any<CancellationToken>()).Returns(_user);
        _hashing.Verify(StrongPassword, "hash").Returns(true);
        _sessions.AddAsync(Arg.Do<UserSession>(s => Identity.Assign(s, new SessionId(30))),
            Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
    }

    [Theory]
    [InlineData("es", "es")]
    [InlineData(" EN ", "en")]
    [InlineData("es-PE", "es")]
    [InlineData("en-US", "en")]
    public void The_language_is_es_or_en(string given, string expected)
    {
        Assert.Equal(expected, new PreferredLanguage(given).Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("fr")]
    [InlineData("español")]
    public void Any_other_language_is_rejected(string? given)
    {
        Assert.Throws<ArgumentException>(() => new PreferredLanguage(given));
    }

    [Fact]
    public void A_new_account_is_in_spanish()
    {
        Assert.Equal("es", _user.PreferredLanguage.Value);
    }

    [Fact]
    public async Task An_invalid_language_fails_before_the_account_is_read()
    {
        var result = await UserService().Handle(new ChangePreferredLanguageCommand(7, "fr"));

        Assert.Equal(IamError.InvalidPreferredLanguage, Assert.IsType<Result<User, IamError>.Failure>(result).Error);
        await _users.DidNotReceiveWithAnyArgs().FindByIdAsync(default, default);
        await _unitOfWork.DidNotReceiveWithAnyArgs().CompleteAsync(default);
    }

    [Fact]
    public async Task A_missing_account_is_UserNotFound()
    {
        var result = await UserService().Handle(new ChangePreferredLanguageCommand(99, "en"));

        Assert.Equal(IamError.UserNotFound, Assert.IsType<Result<User, IamError>.Failure>(result).Error);
    }

    [Fact]
    public async Task After_the_change_the_next_sign_in_returns_the_language_and_the_token_carries_it()
    {
        Assert.True((await UserService().Handle(new ChangePreferredLanguageCommand(7, "en"))).IsSuccess);
        _users.Received(1).Update(_user);
        await _unitOfWork.Received(1).CompleteAsync(Arg.Any<CancellationToken>());

        var signIn = await SessionService().Handle(new SignInCommand("ana@correo.com", StrongPassword));

        var outcome = Assert.IsType<Result<Healthify.Platform.Iam.Application.Internal.SignInOutcome, IamError>.Success>(
            signIn).Value;
        Assert.Equal("en", SignInResponseResourceAssembler.ToResource(outcome).PreferredLanguage);
        Assert.Equal("en", UserResourceAssembler.ToResource(outcome.User).PreferredLanguage);

        var token = new JwtSecurityTokenHandler().ReadJwtToken(outcome.Token);
        Assert.Equal("en", token.Claims.Single(c => c.Type == JwtTokenService.LanguageClaimType).Value);
    }

    [Theory]
    [InlineData("en", null, "Choose Spanish (es) or English (en).")]
    [InlineData("es", null, "Elige Español (es) o English (en).")]
    [InlineData("en", "es-PE", "Elige Español (es) o English (en).")]
    [InlineData("es", "en", "Choose Spanish (es) or English (en).")]
    public async Task Errors_follow_the_claim_only_when_there_is_no_Accept_Language(string claim,
        string? acceptLanguage, string expectedDetail)
    {
        var detail = await ErrorDetailFor(Principal(claim), acceptLanguage);

        Assert.Equal(expectedDetail, detail);
    }

    [Fact]
    public async Task Without_a_session_and_without_a_header_the_platform_default_applies()
    {
        var detail = await ErrorDetailFor(new ClaimsPrincipal(new ClaimsIdentity()), null);

        Assert.Equal("Choose Spanish (es) or English (en).", detail);
    }

    [Fact]
    public async Task The_facade_reads_the_language_and_degrades_to_null()
    {
        var queries = Substitute.For<IUserQueryService>();
        queries.Handle(new GetUserByIdQuery(7), Arg.Any<CancellationToken>()).Returns(_user);
        queries.Handle(new GetUserByIdQuery(8), Arg.Any<CancellationToken>())
            .Returns<User?>(_ => throw new InvalidOperationException("down"));
        var facade = new IamContextFacade(queries);

        Assert.Equal("es", await facade.GetPreferredLanguage(7));
        Assert.Null(await facade.GetPreferredLanguage(8));
        Assert.Null(await facade.GetPreferredLanguage(9));
    }

    /// <summary>
    ///     Runs the real localization middleware with the providers <c>Program.cs</c> configures, then answers
    ///     an IAM error through the real assembler and resource files.
    /// </summary>
    private static async Task<string?> ErrorDetailFor(ClaimsPrincipal principal, string? acceptLanguage)
    {
        string[] supportedCultures = ["en", "en-US", "es", "es-PE"];
        var options = new RequestLocalizationOptions()
            .SetDefaultCulture(supportedCultures[0])
            .AddSupportedCultures(supportedCultures)
            .AddSupportedUICultures(supportedCultures);
        options.RequestCultureProviders.Add(new LanguageClaimRequestCultureProvider());

        var localizer = new StringLocalizer<IamMessages>(new ResourceManagerStringLocalizerFactory(
            Options.Create(new LocalizationOptions()), NullLoggerFactory.Instance));

        string? detail = null;
        var middleware = new RequestLocalizationMiddleware(_ =>
        {
            var result = (ObjectResult)IamActionResultAssembler.ToChangePreferredLanguageResult(
                new Result<User, IamError>.Failure(IamError.InvalidPreferredLanguage), localizer);
            detail = ((ProblemDetails)result.Value!).Detail;
            return Task.CompletedTask;
        }, Options.Create(options), NullLoggerFactory.Instance);

        var context = new DefaultHttpContext { User = principal };
        if (acceptLanguage is not null) context.Request.Headers.AcceptLanguage = acceptLanguage;

        var before = (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture);
        try
        {
            await middleware.Invoke(context);
        }
        finally
        {
            (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture) = before;
        }

        return detail;
    }

    private static ClaimsPrincipal Principal(string language)
    {
        return new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, "7"), new Claim(JwtTokenService.LanguageClaimType, language)],
            "Bearer"));
    }

    private UserCommandService UserService()
    {
        return new UserCommandService(_users, _unitOfWork, _hashing, NullLogger<UserCommandService>.Instance,
            _mediator);
    }

    private UserSessionCommandService SessionService()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["TokenSettings:Secret"] = Secret })
            .Build();
        return new UserSessionCommandService(_users, _sessions, _unitOfWork, _hashing,
            new JwtTokenService(configuration),
            new Healthify.Platform.Iam.Infrastructure.Tokens.Refresh.RefreshTokenService(configuration,
                TimeProvider.System),
            NullLogger<UserSessionCommandService>.Instance, _mediator,
            new Healthify.Platform.Iam.Infrastructure.Security.ConfiguredSignInLockoutPolicy(
                new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build()));
    }
}
