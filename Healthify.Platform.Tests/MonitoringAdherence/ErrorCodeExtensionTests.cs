using Healthify.Platform.MonitoringAdherence.Domain.Model.Errors;
using Healthify.Platform.MonitoringAdherence.Interfaces.REST.Transform;
using Healthify.Platform.MonitoringAdherence.Resources;
using Healthify.Platform.Shared.Application.Ai;
using Healthify.Platform.Shared.Resources;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Localization;
using Xunit;
using static Healthify.Platform.Tests.TestSupport.ProblemResponses;

namespace Healthify.Platform.Tests.MonitoringAdherence;

/// <summary>X-3. Every Monitoring problem carries <c>extensions.code</c>; status, title and detail do not change.</summary>
public class ErrorCodeExtensionTests
{
    private readonly IStringLocalizer<MonitoringMessages> _localizer = EchoLocalizer<MonitoringMessages>();
    private readonly IStringLocalizer<AiMessages> _aiLocalizer = EchoLocalizer<AiMessages>();

    public static TheoryData<MonitoringError> Errors => AllValues<MonitoringError>();
    public static TheoryData<AiError> AiErrors => AllValues<AiError>();

    [Fact]
    public void Not_enough_data_is_a_404_and_a_locked_check_in_a_409()
    {
        var notEnough = Problem(MonitoringActionResultAssembler.ToNotFoundResult(MonitoringError.NotEnoughData,
            _localizer));
        var locked = Problem(MonitoringActionResultAssembler.ToNotFoundResult(MonitoringError.CheckInLocked,
            _localizer));

        Assert.Equal(StatusCodes.Status404NotFound, notEnough.Status);
        Assert.Equal("NotEnoughData", Code(notEnough));
        Assert.Equal(StatusCodes.Status409Conflict, locked.Status);
        Assert.Equal("CheckInLocked", Code(locked));
    }

    [Fact]
    public void An_ai_feature_turned_off_is_a_503_with_its_code_not_internal_error()
    {
        var problem = Problem(MonitoringActionResultAssembler.ToAiFailureResult(AiError.AiFeatureDisabled,
            _aiLocalizer));

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, problem.Status);
        Assert.Equal("AiFeatureDisabled", Code(problem));
    }

    [Theory]
    [MemberData(nameof(Errors))]
    public void Every_error_carries_its_enum_name(MonitoringError error)
    {
        Assert.Equal(ExpectedCode(error),
            Code(Problem(MonitoringActionResultAssembler.ToNotFoundResult(error, _localizer))));
    }

    [Theory]
    [MemberData(nameof(AiErrors))]
    public void Every_ai_error_carries_its_enum_name(AiError error)
    {
        Assert.Equal(ExpectedCode(error),
            Code(Problem(MonitoringActionResultAssembler.ToAiFailureResult(error, _aiLocalizer))));
    }
}
