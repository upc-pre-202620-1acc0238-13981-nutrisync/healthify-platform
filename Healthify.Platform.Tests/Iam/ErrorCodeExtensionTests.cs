using Healthify.Platform.Iam.Application.Internal;
using Healthify.Platform.Iam.Domain.Model.Errors;
using Healthify.Platform.Iam.Interfaces.REST.Transform;
using Healthify.Platform.Iam.Resources;
using Healthify.Platform.Shared.Application.Patterns;
using Microsoft.AspNetCore.Http;
using Xunit;
using static Healthify.Platform.Tests.TestSupport.ProblemResponses;

namespace Healthify.Platform.Tests.Iam;

/// <summary>X-3. Every Iam problem carries <c>extensions.code</c>; status, title and detail do not change.</summary>
public class ErrorCodeExtensionTests
{
    private readonly Microsoft.Extensions.Localization.IStringLocalizer<IamMessages> _localizer =
        EchoLocalizer<IamMessages>();

    public static TheoryData<IamError> Errors => AllValues<IamError>();

    [Fact]
    public void Invalid_credentials_and_account_locked_share_the_401_and_differ_in_their_code()
    {
        var invalid = Problem(IamActionResultAssembler.ToSignInResult(
            new Result<SignInOutcome, IamError>.Failure(IamError.InvalidCredentials), _localizer));
        var locked = Problem(IamActionResultAssembler.ToSignInResult(
            new Result<SignInOutcome, IamError>.Failure(IamError.AccountLocked), _localizer));

        Assert.Equal(StatusCodes.Status401Unauthorized, invalid.Status);
        Assert.Equal(StatusCodes.Status401Unauthorized, locked.Status);
        Assert.Equal("InvalidCredentials", Code(invalid));
        Assert.Equal("AccountLocked", Code(locked));
        Assert.Equal("UnauthorizedTitle", invalid.Title);
        Assert.Equal("InvalidCredentials", invalid.Detail);
        Assert.Equal("AccountLocked", locked.Detail);
    }

    [Fact]
    public void A_domain_validation_error_keeps_its_own_name_not_validation_failed()
    {
        var problem = Problem(IamActionResultAssembler.ToRegisterAccountResult(
            new Result<Healthify.Platform.Iam.Domain.Model.Aggregates.User, IamError>.Failure(IamError.WeakPassword),
            _localizer));

        Assert.Equal(StatusCodes.Status400BadRequest, problem.Status);
        Assert.Equal("WeakPassword", Code(problem));
        Assert.Equal("https://tools.ietf.org/html/rfc7231#section-6.5.1", problem.Type);
    }

    [Theory]
    [MemberData(nameof(Errors))]
    public void Every_error_carries_its_enum_name(IamError error)
    {
        var problem = Problem(IamActionResultAssembler.ToNotFoundResult(error, _localizer));

        Assert.Equal(ExpectedCode(error), Code(problem));
        if (problem.Status == StatusCodes.Status500InternalServerError) Assert.Equal("InternalError", Code(problem));
    }
}
