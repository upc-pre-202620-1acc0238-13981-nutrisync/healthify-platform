using Healthify.Platform.NutritionalCare.Domain.Model.Aggregates;
using Healthify.Platform.NutritionalCare.Domain.Model.Errors;
using Healthify.Platform.NutritionalCare.Interfaces.REST.Transform;
using Healthify.Platform.NutritionalCare.Resources;
using Healthify.Platform.Shared.Application.Ai;
using Healthify.Platform.Shared.Application.Patterns;
using Healthify.Platform.Shared.Resources;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Localization;
using Xunit;
using static Healthify.Platform.Tests.TestSupport.ProblemResponses;

namespace Healthify.Platform.Tests.NutritionalCare;

/// <summary>X-3. Every NutritionalCare problem carries <c>extensions.code</c>; status, title and detail do not change.</summary>
public class ErrorCodeExtensionTests
{
    private readonly IStringLocalizer<NutritionalCareMessages> _localizer = EchoLocalizer<NutritionalCareMessages>();
    private readonly IStringLocalizer<AiMessages> _aiLocalizer = EchoLocalizer<AiMessages>();

    public static TheoryData<NutritionalCareError> Errors => AllValues<NutritionalCareError>();
    public static TheoryData<AiError> AiErrors => AllValues<AiError>();

    [Fact]
    public void A_second_consultation_is_a_409_with_its_code()
    {
        var problem = Problem(NutritionalCareActionResultAssembler.ToStartConsultationResult(
            new Result<Consultation, NutritionalCareError>.Failure(NutritionalCareError.ConsultationAlreadyInProgress),
            null, _localizer));

        Assert.Equal(StatusCodes.Status409Conflict, problem.Status);
        Assert.Equal("ConsultationAlreadyInProgress", Code(problem));
    }

    [Fact]
    public void A_step_out_of_order_is_a_422_and_an_unexpected_error_a_500_internal_error()
    {
        var outOfOrder = Problem(NutritionalCareActionResultAssembler.ToErrorResult(
            NutritionalCareError.ConsultationStepOutOfOrder, _localizer));
        var unexpected = Problem(NutritionalCareActionResultAssembler.ToErrorResult(
            NutritionalCareError.UnexpectedError, _localizer));

        Assert.Equal(StatusCodes.Status422UnprocessableEntity, outOfOrder.Status);
        Assert.Equal("ConsultationStepOutOfOrder", Code(outOfOrder));
        Assert.Equal(StatusCodes.Status500InternalServerError, unexpected.Status);
        Assert.Equal("InternalError", Code(unexpected));
        Assert.Equal("UnexpectedError", unexpected.Detail);
    }

    [Theory]
    [MemberData(nameof(Errors))]
    public void Every_error_carries_its_enum_name(NutritionalCareError error)
    {
        Assert.Equal(ExpectedCode(error),
            Code(Problem(NutritionalCareActionResultAssembler.ToErrorResult(error, _localizer))));
    }

    [Theory]
    [MemberData(nameof(AiErrors))]
    public void Every_ai_error_carries_its_enum_name(AiError error)
    {
        Assert.Equal(ExpectedCode(error),
            Code(Problem(NutritionalCareActionResultAssembler.ToAiFailureResult(error, _aiLocalizer))));
    }
}
