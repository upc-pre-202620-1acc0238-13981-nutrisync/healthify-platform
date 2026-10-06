using Healthify.Platform.IntakeBodyResponse.Application.QueryServices;
using Healthify.Platform.IntakeBodyResponse.Domain.Model.Aggregates;
using Healthify.Platform.IntakeBodyResponse.Domain.Model.Errors;
using Healthify.Platform.IntakeBodyResponse.Domain.Model.Queries;
using Healthify.Platform.IntakeBodyResponse.Domain.Model.ValueObjects;
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
///     The per-patient read models of the Intake and Body Response bounded context: My Daily Targets,
///     Daily Diary, Weight Trend Chart and Pending Sync Queue.
/// </summary>
/// <remarks>
///     Patient only. The practitioner reads the same material through the composite monitoring panel,
///     which is assembled from the ACL contract of this context rather than from these endpoints.
///     The route carries the patient identifier because that is the shape of the read model; the
///     identity that is trusted is always the one in the token.
/// </remarks>
[ApiController]
[Route("api/v1/patients")]
[Authorize(Roles = "Patient")]
[Tags("Intake and Body Response")]
[Produces("application/json")]
public class PatientIntakeController(
    IActiveTargetsCacheQueryService activeTargetsQueryService,
    IDiaryEntryQueryService diaryEntryQueryService,
    IWeightTrendQueryService weightTrendQueryService,
    IStringLocalizer<IntakeMessages> localizer) : ControllerBase
{
    [HttpGet("{patientId:int}/active-targets")]
    [SwaggerOperation(
        Summary = "Get the active targets of a patient",
        Description =
            "Returns everything the patient sees about their plan: the targets, the guidelines and the " +
            "restrictions. It never includes the diagnosis, the clinical reasoning or how the numbers were " +
            "calculated. The app keeps a copy so it works without a connection.")]
    [SwaggerResponse(StatusCodes.Status200OK, "The active targets.", typeof(ActiveTargetsResource))]
    [SwaggerResponse(StatusCodes.Status401Unauthorized, "Authentication is required.")]
    [SwaggerResponse(StatusCodes.Status403Forbidden, "The targets of another patient cannot be read.",
        typeof(ProblemDetails))]
    [SwaggerResponse(StatusCodes.Status404NotFound, "No targets have been published yet.",
        typeof(ProblemDetails))]
    public async Task<IActionResult> GetActiveTargets(int patientId)
    {
        if (!IsSelf(patientId)) return NotThisPatient();

        var cache = await activeTargetsQueryService.Handle(
            new GetActiveTargetsByPatientIdQuery(patientId), HttpContext.RequestAborted);

        if (cache is null)
            return IntakeActionResultAssembler.ToNotFoundResult(
                IntakeError.ActiveTargetsCacheNotFound, localizer);

        return Ok(ActiveTargetsResourceAssembler.ToResource(cache));
    }

    [HttpGet("{patientId:int}/diary-entries")]
    [SwaggerOperation(
        Summary = "Get the diary of a patient",
        Description =
            "Returns the patient's diary. Each entry says how it was logged and, when there is an estimate, how " +
            "confident it is. Filtering by date uses the patient's own day, not the server's. Nothing in the " +
            "response judges whether a meal was a good idea.")]
    [SwaggerResponse(StatusCodes.Status200OK, "The entries, most recent first.",
        typeof(IEnumerable<DiaryEntryResource>))]
    [SwaggerResponse(StatusCodes.Status401Unauthorized, "Authentication is required.")]
    [SwaggerResponse(StatusCodes.Status403Forbidden, "The diary of another patient cannot be read.",
        typeof(ProblemDetails))]
    public async Task<IActionResult> GetDiaryEntries(int patientId, [FromQuery] DateOnly? date)
    {
        if (!IsSelf(patientId)) return NotThisPatient();

        var entries = (await diaryEntryQueryService.Handle(
            new GetDiaryEntriesByPatientIdQuery(patientId, date), HttpContext.RequestAborted)).ToList();

        return Ok(DiaryEntryResourceAssembler.ToResources(entries, await FoodNamesOf(entries)));
    }

    [HttpGet("{patientId:int}/weight-trend")]
    [SwaggerOperation(
        Summary = "Get the weight trend of a patient",
        Description =
            "Returns the smoothed trend of the patient's home weigh-ins. There is no 'today's weight' and no " +
            "'latest reading', because a single morning reading can move several hundred grams just from hydration " +
            "and should not read as a verdict. The weeks parameter (4 by default, from 1 to 52) sets the period of " +
            "the summary: how much the weight changed, the change per week, and how many readings were left out of " +
            "the line because they were not taken fasting. The full series is always returned.")]
    [SwaggerResponse(StatusCodes.Status200OK, "The smoothed series.", typeof(WeightTrendResource))]
    [SwaggerResponse(StatusCodes.Status401Unauthorized, "Authentication is required.")]
    [SwaggerResponse(StatusCodes.Status403Forbidden, "The trend of another patient cannot be read.",
        typeof(ProblemDetails))]
    [SwaggerResponse(StatusCodes.Status404NotFound, "No trend has been calculated yet.",
        typeof(ProblemDetails))]
    public async Task<IActionResult> GetWeightTrend(int patientId,
        [FromQuery] int weeks = WeightTrendRange.DefaultWeeks)
    {
        if (!IsSelf(patientId)) return NotThisPatient();

        var view = await weightTrendQueryService.Handle(
            new GetWeightTrendRangeByPatientIdQuery(patientId, weeks), HttpContext.RequestAborted);

        if (view is null)
            return IntakeActionResultAssembler.ToNotFoundResult(IntakeError.WeightTrendNotFound,
                localizer);

        return Ok(WeightTrendResourceAssembler.ToResource(view));
    }

    [HttpGet("{patientId:int}/pending-sync-queue")]
    [SwaggerOperation(
        Summary = "Get the entries still waiting to be reconciled",
        Description =
            "Returns the meals that are still waiting to be reconciled after an offline sync. An empty list is the " +
            "normal answer: meals only stay here when a sync was interrupted halfway.")]
    [SwaggerResponse(StatusCodes.Status200OK, "The unreconciled entries, oldest first.",
        typeof(IEnumerable<DiaryEntryResource>))]
    [SwaggerResponse(StatusCodes.Status401Unauthorized, "Authentication is required.")]
    [SwaggerResponse(StatusCodes.Status403Forbidden, "The queue of another patient cannot be read.",
        typeof(ProblemDetails))]
    public async Task<IActionResult> GetPendingSyncQueue(int patientId)
    {
        if (!IsSelf(patientId)) return NotThisPatient();

        var entries = (await diaryEntryQueryService.Handle(
            new GetPendingSyncQueueByPatientIdQuery(patientId), HttpContext.RequestAborted)).ToList();

        return Ok(DiaryEntryResourceAssembler.ToResources(entries, await FoodNamesOf(entries)));
    }

    /// <summary>IN-1. The names of every food shown, resolved in one query rather than per entry.</summary>
    private async Task<IReadOnlyDictionary<int, string>> FoodNamesOf(IEnumerable<DiaryEntry> entries)
    {
        var ids = entries.Select(DiaryEntryResourceAssembler.DisplayedFoodId).OfType<int>().ToList();
        return await diaryEntryQueryService.Handle(new GetFoodNamesQuery(ids), HttpContext.RequestAborted);
    }

    private bool IsSelf(int patientId)
    {
        return this.GetAuthenticatedUserId() == patientId;
    }

    private IActionResult NotThisPatient()
    {
        return IntakeActionResultAssembler.ToNotFoundResult(IntakeError.PatientWriteOnly, localizer);
    }
}
