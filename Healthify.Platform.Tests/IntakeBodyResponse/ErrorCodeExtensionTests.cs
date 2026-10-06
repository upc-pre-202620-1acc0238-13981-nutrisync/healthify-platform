using Healthify.Platform.IntakeBodyResponse.Domain.Model.Errors;
using Healthify.Platform.IntakeBodyResponse.Interfaces.REST.Transform;
using Healthify.Platform.IntakeBodyResponse.Resources;
using Healthify.Platform.Shared.Application.Ai;
using Healthify.Platform.Shared.Resources;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Localization;
using Xunit;
using static Healthify.Platform.Tests.TestSupport.ProblemResponses;

namespace Healthify.Platform.Tests.IntakeBodyResponse;

/// <summary>X-3. Every Intake problem carries <c>extensions.code</c>; status, title and detail do not change.</summary>
public class ErrorCodeExtensionTests
{
    private readonly IStringLocalizer<IntakeMessages> _localizer = EchoLocalizer<IntakeMessages>();
    private readonly IStringLocalizer<AiMessages> _aiLocalizer = EchoLocalizer<AiMessages>();

    public static TheoryData<IntakeError> Errors => AllValues<IntakeError>();
    public static TheoryData<AiError> AiErrors => AllValues<AiError>();

    [Fact]
    public void A_photo_too_large_is_a_413_and_an_unrecognized_one_a_422()
    {
        var tooLarge = Problem(IntakeActionResultAssembler.ToNotFoundResult(IntakeError.PhotoTooLarge, _localizer));
        var notRecognized = Problem(IntakeActionResultAssembler.ToNotFoundResult(IntakeError.PhotoNotRecognized,
            _localizer));

        Assert.Equal(StatusCodes.Status413PayloadTooLarge, tooLarge.Status);
        Assert.Equal("PhotoTooLarge", Code(tooLarge));
        Assert.Equal(StatusCodes.Status422UnprocessableEntity, notRecognized.Status);
        Assert.Equal("PhotoNotRecognized", Code(notRecognized));
    }

    [Fact]
    public void The_daily_ai_quota_is_a_429_ai_rate_limited()
    {
        var problem = Problem(IntakeActionResultAssembler.ToAiFailureResult(AiError.AiRateLimited, _aiLocalizer));

        Assert.Equal(StatusCodes.Status429TooManyRequests, problem.Status);
        Assert.Equal("AiRateLimited", Code(problem));
        Assert.Equal("AiRateLimited", problem.Detail);
    }

    [Theory]
    [MemberData(nameof(Errors))]
    public void Every_error_carries_its_enum_name(IntakeError error)
    {
        Assert.Equal(ExpectedCode(error),
            Code(Problem(IntakeActionResultAssembler.ToNotFoundResult(error, _localizer))));
    }

    [Theory]
    [MemberData(nameof(AiErrors))]
    public void Every_ai_error_carries_its_enum_name(AiError error)
    {
        Assert.Equal(ExpectedCode(error),
            Code(Problem(IntakeActionResultAssembler.ToAiFailureResult(error, _aiLocalizer))));
    }
}
