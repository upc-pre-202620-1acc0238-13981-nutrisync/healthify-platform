using Healthify.Platform.IntakeBodyResponse.Application.CommandServices;
using Healthify.Platform.IntakeBodyResponse.Domain.Model.Errors;
using Healthify.Platform.IntakeBodyResponse.Domain.Services;
using Healthify.Platform.IntakeBodyResponse.Interfaces.REST.Resources;
using Healthify.Platform.IntakeBodyResponse.Interfaces.REST.Transform;
using Healthify.Platform.IntakeBodyResponse.Resources;
using Healthify.Platform.Shared.Interfaces.REST.Extensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Swashbuckle.AspNetCore.Annotations;
using ProblemDetails = Microsoft.AspNetCore.Mvc.ProblemDetails;

namespace Healthify.Platform.IntakeBodyResponse.Interfaces.REST;

/// <summary>
///     Subflow 4.5 of the Intake and Body Response bounded context.
/// </summary>
/// <remarks>
///     Patient only. A self weigh-in is not a clinical measurement and this platform never confuses
///     the two: the clinical reading is taken by the practitioner in Nutritional Care, under a
///     recorded protocol, and it outranks this one wherever both exist.
///     Recalculate Weight Trend has no endpoint. It is reached only from the policy that fires when a
///     reading is recorded.
/// </remarks>
[ApiController]
[Route("api/v1/self-weigh-ins")]
[Authorize(Roles = "Patient")]
[Tags("Intake and Body Response")]
[Produces("application/json")]
public class SelfWeighInsController(
    ISelfWeighInCommandService commandService,
    ISelfWeighInProtocolProvider protocolProvider,
    IStringLocalizer<IntakeMessages> localizer) : ControllerBase
{
    [HttpPost]
    [Consumes("application/json")]
    [SwaggerOperation(
        Summary = "Record a self weigh-in",
        Description =
            "Records a weight the patient took at home, together with whether they weighed themselves fasting. " +
            "Older versions of the app can also say whether it was at the same time of day and on the same scale. A " +
            "reading not taken fasting is still saved in full; it simply does not count for the trend. Nothing is " +
            "rejected and nothing is flagged.")]
    [SwaggerResponse(StatusCodes.Status201Created, "The reading was recorded.",
        typeof(SelfWeighInResource))]
    [SwaggerResponse(StatusCodes.Status400BadRequest,
        "The reading is outside the plausible range, or the moment is missing.", typeof(ProblemDetails))]
    [SwaggerResponse(StatusCodes.Status401Unauthorized, "Authentication is required.")]
    [SwaggerResponse(StatusCodes.Status403Forbidden, "Only the patient records their own readings.",
        typeof(ProblemDetails))]
    public async Task<IActionResult> RecordSelfWeighIn([FromBody] RecordSelfWeighInResource resource)
    {
        var patientId = this.GetAuthenticatedUserId();
        if (resource.PatientId != patientId)
            return IntakeActionResultAssembler.ToNotFoundResult(IntakeError.PatientWriteOnly, localizer);

        var result = await commandService.Handle(
            RecordSelfWeighInCommandAssembler.ToCommand(patientId, resource),
            HttpContext.RequestAborted);

        return IntakeActionResultAssembler.ToSelfWeighInResult(result, localizer,
            StatusCodes.Status201Created, protocolProvider.Current);
    }

    [HttpPost("synchronization")]
    [Consumes("application/json")]
    [SwaggerOperation(
        Summary = "Synchronise the self weigh-ins queued offline",
        Description =
            "Sends the home weigh-ins the app saved while it was offline. Each one carries an identifier from the " +
            "app, so a reading sent twice, or repeated in the same batch, is recognised and saved only once. " +
            "Readings are never edited, so the first copy stays. Each reading is processed and reported on its own; " +
            "the batch never fails as a whole. The weight trend is recalculated once for the whole batch.")]
    [SwaggerResponse(StatusCodes.Status200OK, "The batch was reconciled, reading by reading.",
        typeof(SelfWeighInSyncOutcomeResource))]
    [SwaggerResponse(StatusCodes.Status401Unauthorized, "Authentication is required.")]
    [SwaggerResponse(StatusCodes.Status403Forbidden, "Only the patient synchronises their own readings.",
        typeof(ProblemDetails))]
    public async Task<IActionResult> SyncSelfWeighIns([FromBody] SyncSelfWeighInsResource resource)
    {
        var patientId = this.GetAuthenticatedUserId();
        if (resource.PatientId is { } declared && declared != patientId)
            return IntakeActionResultAssembler.ToNotFoundResult(IntakeError.PatientWriteOnly, localizer);

        var result = await commandService.Handle(
            SyncSelfWeighInsCommandAssembler.ToCommand(patientId, resource),
            HttpContext.RequestAborted);

        return IntakeActionResultAssembler.ToSelfWeighInSyncResult(result, localizer);
    }
}
