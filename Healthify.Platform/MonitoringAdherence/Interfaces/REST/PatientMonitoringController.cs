using Healthify.Platform.CareRelationship.Interfaces.Acl;
using Healthify.Platform.MonitoringAdherence.Application.QueryServices;
using Healthify.Platform.MonitoringAdherence.Domain.Model.Errors;
using Healthify.Platform.MonitoringAdherence.Domain.Model.Queries;
using Healthify.Platform.MonitoringAdherence.Interfaces.REST.Resources;
using Healthify.Platform.MonitoringAdherence.Interfaces.REST.Transform;
using Healthify.Platform.MonitoringAdherence.Resources;
using Healthify.Platform.Shared.Interfaces.REST.Extensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Swashbuckle.AspNetCore.Annotations;
using ProblemDetails = Microsoft.AspNetCore.Mvc.ProblemDetails;

namespace Healthify.Platform.MonitoringAdherence.Interfaces.REST;

/// <summary>
///     The per-patient read models of the Monitoring and Adherence bounded context.
/// </summary>
/// <remarks>
///     Nothing here writes. Every command of Subflows 5.1 to 5.9 and 5.11 is driven by a policy or by
///     the passage of time, because nobody decides to evaluate a day: it happens because a day
///     happened.
///     The two roles read different things and that asymmetry is on the actions rather than on the
///     controller. The daily indicator and the consistency card belong to the patient; the deviations
///     belong to the practitioner, and they only exist once a tendency has persisted. The controller
///     carries no blanket role attribute, because a general attribute here would silently widen or
///     narrow an action below it.
/// </remarks>
[ApiController]
[Route("api/v1/patients")]
[Authorize]
[Tags("Monitoring and Adherence")]
[Produces("application/json")]
public class PatientMonitoringController(
    IEvaluationWindowQueryService evaluationWindowQueryService,
    IDeviationQueryService deviationQueryService,
    IConsistencyIndexQueryService consistencyIndexQueryService,
    IReferralQueryService referralQueryService,
    ICareRelationshipContextFacade careRelationshipContextFacade,
    IStringLocalizer<MonitoringMessages> localizer) : ControllerBase
{
    [HttpGet("{patientId:int}/evaluation-windows")]
    [SwaggerOperation(
        Summary = "Get the evaluation windows of a patient",
        Description =
            "Returns every follow-up period of the patient, including closed ones: what was evaluated during a care " +
            "relationship that ended is kept. Each period has its snapshots, its day-by-day results and the clinic " +
            "measurements as separate lists, because clinic and home weights are never mixed.")]
    [SwaggerResponse(StatusCodes.Status200OK, "The windows, most recent first.",
        typeof(IEnumerable<EvaluationWindowResource>))]
    [SwaggerResponse(StatusCodes.Status401Unauthorized, "Authentication is required.")]
    [SwaggerResponse(StatusCodes.Status403Forbidden, "This patient cannot be read from this session.",
        typeof(ProblemDetails))]
    public async Task<IActionResult> GetEvaluationWindows(int patientId)
    {
        if (!await MayRead(patientId)) return NotAllowed();

        var windows = await evaluationWindowQueryService.Handle(
            new GetEvaluationWindowsByPatientIdQuery(patientId), HttpContext.RequestAborted);

        return Ok(windows.Select(EvaluationWindowResourceAssembler.ToResource));
    }

    [HttpGet("{patientId:int}/evaluation-windows/current")]
    [SwaggerOperation(
        Summary = "Get the evaluation window that is still counting",
        Description =
            "Returns the follow-up period that is still running. A patient has at most one: it starts when the care " +
            "link is created and ends when the link ends.")]
    [SwaggerResponse(StatusCodes.Status200OK, "The window that is still counting.",
        typeof(EvaluationWindowResource))]
    [SwaggerResponse(StatusCodes.Status401Unauthorized, "Authentication is required.")]
    [SwaggerResponse(StatusCodes.Status403Forbidden, "This patient cannot be read from this session.",
        typeof(ProblemDetails))]
    [SwaggerResponse(StatusCodes.Status404NotFound, "No window is counting days for this patient.",
        typeof(ProblemDetails))]
    public async Task<IActionResult> GetCurrentEvaluationWindow(int patientId)
    {
        if (!await MayRead(patientId)) return NotAllowed();

        var window = await evaluationWindowQueryService.Handle(
            new GetCurrentEvaluationWindowByPatientIdQuery(patientId), HttpContext.RequestAborted);

        if (window is null)
            return MonitoringActionResultAssembler.ToNotFoundResult(
                MonitoringError.EvaluationWindowNotFound, localizer);

        return Ok(EvaluationWindowResourceAssembler.ToResource(window));
    }

    [HttpGet("{patientId:int}/daily-compliance")]
    [Authorize(Roles = "Patient")]
    [SwaggerOperation(
        Summary = "Get the day-by-day outcome of a patient",
        Description =
            "Returns how each day went against the targets: met, exceeded, short or not logged. A day with an empty " +
            "diary is 'not logged', which is not a failure: it is left out of every deviation and never raises an " +
            "alert. There are no streaks and no penalties. With from and to (both included, at most 31 days), every " +
            "calendar day of the period is returned together with a summary such as '5 of 7 days'. Without them, or " +
            "with a single date, the original list is returned for older versions of the app.")]
    [SwaggerResponse(StatusCodes.Status200OK,
        "The days, oldest first; with from and to, every day of the period and its summary.",
        typeof(IEnumerable<DailyComplianceResource>))]
    [SwaggerResponse(StatusCodes.Status400BadRequest,
        "Only one end of the range, ends out of order, more than 31 days, or date together with the range.",
        typeof(ProblemDetails))]
    [SwaggerResponse(StatusCodes.Status401Unauthorized, "Authentication is required.")]
    [SwaggerResponse(StatusCodes.Status403Forbidden, "The days of another patient cannot be read.",
        typeof(ProblemDetails))]
    public async Task<IActionResult> GetDailyCompliance(int patientId, [FromQuery] DateOnly? date,
        [FromQuery] DateOnly? from = null, [FromQuery] DateOnly? to = null)
    {
        if (!IsSelf(patientId)) return NotAllowed();

        if (from is not null || to is not null)
        {
            if (date is not null || !DailyComplianceRangeQueryAssembler.TryToQuery(patientId, from, to, out var query))
                return MonitoringActionResultAssembler.ToNotFoundResult(MonitoringError.InvalidComplianceRange,
                    localizer);

            var range = await evaluationWindowQueryService.Handle(query!, HttpContext.RequestAborted);
            return Ok(DailyComplianceRangeResourceAssembler.ToResource(range));
        }

        var days = await evaluationWindowQueryService.Handle(
            new GetDailyComplianceByPatientIdQuery(patientId, date), HttpContext.RequestAborted);

        return Ok(days.Select(DailyComplianceResourceAssembler.ToResource));
    }

    [HttpGet("{patientId:int}/deviations")]
    [Authorize(Roles = "Practitioner")]
    [SwaggerOperation(
        Summary = "Get the deviations read from a patient window",
        Description =
            "Returns the deviations found in the patient's follow-up period. A deviation is only measured over at " +
            "least seven days and only from the days the patient actually logged: one bad day is not a deviation. " +
            "Only a sustained deviation reaches the practitioner's review inbox, and even then it only notifies: " +
            "nothing here changes a plan.")]
    [SwaggerResponse(StatusCodes.Status200OK, "The deviations, most recent first.",
        typeof(IEnumerable<DeviationResource>))]
    [SwaggerResponse(StatusCodes.Status401Unauthorized, "Authentication is required.")]
    [SwaggerResponse(StatusCodes.Status403Forbidden, "There is no active care link with this patient.",
        typeof(ProblemDetails))]
    public async Task<IActionResult> GetDeviations(int patientId)
    {
        if (!await HasActiveCareLink(patientId)) return NotAllowed();

        var deviations = await deviationQueryService.Handle(
            new GetDeviationsByPatientIdQuery(patientId), HttpContext.RequestAborted);

        return Ok(deviations.Select(DeviationResourceAssembler.ToResource));
    }

    [HttpGet("{patientId:int}/consistency-index")]
    [Authorize(Roles = "Patient")]
    [SwaggerOperation(
        Summary = "Get the consistency index of a patient",
        Description =
            "Returns the index that compares the weight trend with the logged intake. It needs both, so with only " +
            "one of them there is no index. The patient always sees it first, and it reaches the practitioner only " +
            "after three weeks in alert. Both dates are in the response, so nobody is watched without knowing.")]
    [SwaggerResponse(StatusCodes.Status200OK, "The index.", typeof(ConsistencyIndexResource))]
    [SwaggerResponse(StatusCodes.Status401Unauthorized, "Authentication is required.")]
    [SwaggerResponse(StatusCodes.Status403Forbidden, "The index of another patient cannot be read.",
        typeof(ProblemDetails))]
    [SwaggerResponse(StatusCodes.Status422UnprocessableEntity,
        "There is no index yet, because both series are needed.", typeof(ProblemDetails))]
    public async Task<IActionResult> GetConsistencyIndex(int patientId)
    {
        if (!IsSelf(patientId)) return NotAllowed();

        var index = await consistencyIndexQueryService.Handle(
            new GetConsistencyIndexByPatientIdQuery(patientId), HttpContext.RequestAborted);

        if (index is null)
            return MonitoringActionResultAssembler.ToNotFoundResult(
                MonitoringError.BothSeriesRequired, localizer);

        return Ok(ConsistencyIndexResourceAssembler.ToResource(index));
    }

    [HttpGet("{patientId:int}/referrals")]
    [SwaggerOperation(
        Summary = "Get the referrals of a patient",
        Description =
            "Returns the referrals the practitioner recorded for the patient. A referral records what was decided " +
            "and when; what happens afterwards happens outside this platform.")]
    [SwaggerResponse(StatusCodes.Status200OK, "The referrals, most recent first.",
        typeof(IEnumerable<ReferralResource>))]
    [SwaggerResponse(StatusCodes.Status401Unauthorized, "Authentication is required.")]
    [SwaggerResponse(StatusCodes.Status403Forbidden, "This patient cannot be read from this session.",
        typeof(ProblemDetails))]
    public async Task<IActionResult> GetReferrals(int patientId)
    {
        if (!await MayRead(patientId)) return NotAllowed();

        var referrals = await referralQueryService.Handle(new GetReferralsByPatientIdQuery(patientId),
            HttpContext.RequestAborted);

        return Ok(referrals.Select(ReferralResourceAssembler.ToResource));
    }

    /// <summary>
    ///     The patient reads their own, and the practitioner reads the patients they are linked to.
    ///     Both questions are answered where they are owned: the identity in the token, and the care
    ///     link in the context that owns it.
    /// </summary>
    private async Task<bool> MayRead(int patientId)
    {
        if (IsSelf(patientId)) return true;
        return await HasActiveCareLink(patientId);
    }

    private async Task<bool> HasActiveCareLink(int patientId)
    {
        return await careRelationshipContextFacade.IsCareLinkActive(patientId,
            this.GetAuthenticatedUserId(), HttpContext.RequestAborted);
    }

    private bool IsSelf(int patientId)
    {
        return this.GetAuthenticatedUserId() == patientId;
    }

    private IActionResult NotAllowed()
    {
        return MonitoringActionResultAssembler.ToNotFoundResult(
            MonitoringError.ActiveCareLinkRequired, localizer);
    }
}
