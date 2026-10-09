using Healthify.Platform.CareRelationship.Application.Internal;
using Healthify.Platform.CareRelationship.Domain.Model.Errors;
using Healthify.Platform.CareRelationship.Interfaces.REST.Transform;
using Healthify.Platform.CareRelationship.Resources;
using Healthify.Platform.Shared.Application.Ai;
using Healthify.Platform.Shared.Application.Patterns;
using Healthify.Platform.Shared.Resources;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Localization;
using Xunit;
using static Healthify.Platform.Tests.TestSupport.ProblemResponses;

namespace Healthify.Platform.Tests.CareRelationship;

/// <summary>X-3. Every CareRelationship problem carries <c>extensions.code</c>; status, title and detail do not change.</summary>
public class ErrorCodeExtensionTests
{
    private readonly IStringLocalizer<CareRelationshipMessages> _localizer = EchoLocalizer<CareRelationshipMessages>();
    private readonly IStringLocalizer<AiMessages> _aiLocalizer = EchoLocalizer<AiMessages>();

    public static TheoryData<CareRelationshipError> Errors => AllValues<CareRelationshipError>();
    public static TheoryData<AiError> AiErrors => AllValues<AiError>();

    [Fact]
    public void Enabling_a_preference_without_consent_is_a_409_with_its_code()
    {
        var problem = Problem(CareRelationshipActionResultAssembler.ToAiPreferencesResult(
            new Result<AiPreferencesStatus, CareRelationshipError>.Failure(
                CareRelationshipError.AiConsentRequiredToEnableFeature), _localizer));

        Assert.Equal(StatusCodes.Status409Conflict, problem.Status);
        Assert.Equal("AiConsentRequiredToEnableFeature", Code(problem));
        Assert.Equal("AiConsentRequiredToEnableFeature", problem.Detail);
    }

    [Fact]
    public void The_pipeline_refusal_is_a_403_ai_consent_required_and_an_expired_invitation_a_422()
    {
        var consent = Problem(CareRelationshipActionResultAssembler.ToAiFailureResult(AiError.AiConsentRequired,
            _aiLocalizer));
        var expired = Problem(CareRelationshipActionResultAssembler.ToNotFoundResult(
            CareRelationshipError.InvitationExpired, _localizer));

        Assert.Equal(StatusCodes.Status403Forbidden, consent.Status);
        Assert.Equal("AiConsentRequired", Code(consent));
        Assert.Equal(StatusCodes.Status422UnprocessableEntity, expired.Status);
        Assert.Equal("InvitationExpired", Code(expired));
    }

    [Theory]
    [MemberData(nameof(Errors))]
    public void Every_error_carries_its_enum_name(CareRelationshipError error)
    {
        Assert.Equal(ExpectedCode(error),
            Code(Problem(CareRelationshipActionResultAssembler.ToNotFoundResult(error, _localizer))));
    }

    [Theory]
    [MemberData(nameof(AiErrors))]
    public void Every_ai_error_carries_its_enum_name(AiError error)
    {
        Assert.Equal(ExpectedCode(error),
            Code(Problem(CareRelationshipActionResultAssembler.ToAiFailureResult(error, _aiLocalizer))));
    }
}
