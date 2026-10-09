using Healthify.Platform.IntakeBodyResponse.Application.CommandServices;
using Healthify.Platform.IntakeBodyResponse.Application.Internal;
using Healthify.Platform.IntakeBodyResponse.Application.QueryServices;
using Healthify.Platform.IntakeBodyResponse.Domain.Model.Aggregates;
using Healthify.Platform.IntakeBodyResponse.Domain.Model.Errors;
using Healthify.Platform.IntakeBodyResponse.Domain.Model.Queries;
using Healthify.Platform.IntakeBodyResponse.Interfaces.REST.Resources;
using Healthify.Platform.IntakeBodyResponse.Interfaces.REST.Transform;
using Healthify.Platform.IntakeBodyResponse.Resources;
using Healthify.Platform.Shared.Interfaces.REST.Extensions;
using Microsoft.AspNetCore.Authorization;
using Healthify.Platform.Shared.Application.Patterns;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.Localization;
using Swashbuckle.AspNetCore.Annotations;
using ProblemDetails = Microsoft.AspNetCore.Mvc.ProblemDetails;

namespace Healthify.Platform.IntakeBodyResponse.Interfaces.REST;

/// <summary>
///     Subflows 4.2, 4.3, 4.4 and 4.6 of the Intake and Body Response bounded context.
/// </summary>
/// <remarks>
///     Patient only, without exception, and that is a design decision rather than a permission
///     setting. The practitioner reads this diary through the monitoring panel, with confidence and
///     provenance in plain sight, and interprets it qualitatively. They do not write to it, and there
///     is no endpoint on this controller that would let them.
///     There is no DELETE here either. An entry the patient regrets is still an entry, and a diary
///     that can be pruned cannot be interpreted afterwards.
///     Estimate Portion has no endpoint: it is reached only from the policy that fires when a photo
///     entry is logged.
/// </remarks>
[ApiController]
[Route("api/v1/diary-entries")]
[Authorize(Roles = "Patient")]
[Tags("Intake and Body Response")]
[Produces("application/json")]
public class DiaryEntriesController(
    IDiaryEntryCommandService commandService,
    IDiaryEntryQueryService queryService,
    IStringLocalizer<IntakeMessages> localizer) : ControllerBase
{
    [HttpPost("photo-logs")]
    [Consumes("application/json")]
    [SwaggerOperation(
        Summary = "Log a meal from a photo",
        Description =
            "Logs a meal from a photo. The app sends the food, the portion and how confident the estimate is (the " +
            "confidence is required), and they are saved as a proposal, not yet as what the patient ate. If the " +
            "patient already confirmed or corrected the estimate, the app can send that confirmation together with " +
            "whether the meal was in the plan, and the meal is saved already confirmed, with the original proposal " +
            "kept beside it. Without a confirmation the meal is saved as pending confirmation. If the photo was " +
            "analysed on the server first, the app sends the identifier of that analysis instead, and the proposal " +
            "is the one from the analysis; the analysis must belong to the patient and must not have expired, and " +
            "logging it twice returns the same entry. If the app sends its own identifier for the entry, sending " +
            "the same meal again returns the entry already saved instead of creating another one.")]
    [SwaggerResponse(StatusCodes.Status201Created, "The entry was logged.", typeof(DiaryEntryResource))]
    [SwaggerResponse(StatusCodes.Status400BadRequest,
        "The confidence or the time is missing, the confirmation is not valid, or it was not said whether the " +
        "meal is in the plan.",
        typeof(ProblemDetails))]
    [SwaggerResponse(StatusCodes.Status401Unauthorized, "Authentication is required.")]
    [SwaggerResponse(StatusCodes.Status403Forbidden, "Only the patient writes their own diary.",
        typeof(ProblemDetails))]
    [SwaggerResponse(StatusCodes.Status404NotFound, "The photo analysis was not found for this patient.",
        typeof(ProblemDetails))]
    [SwaggerResponse(StatusCodes.Status409Conflict,
        "The identifier the app sent for the entry belongs to another patient's entry.",
        typeof(ProblemDetails))]
    [SwaggerResponse(StatusCodes.Status422UnprocessableEntity,
        "The food was not found, the time is too far in the past, or the photo analysis expired.",
        typeof(ProblemDetails))]
    public async Task<IActionResult> LogMealByPhoto([FromBody] LogMealByPhotoResource resource)
    {
        var patientId = this.GetAuthenticatedUserId();
        if (resource.PatientId != patientId) return PatientWriteOnly();

        var result = await commandService.Handle(
            LogMealByPhotoCommandAssembler.ToCommand(patientId, resource), HttpContext.RequestAborted);

        return IntakeActionResultAssembler.ToDiaryEntryResult(result, localizer,
            StatusCodes.Status201Created, await FoodNameOf(result));
    }

    [HttpPost("manual-logs")]
    [Consumes("application/json")]
    [SwaggerOperation(
        Summary = "Log a meal by hand",
        Description =
            "Logs a meal the patient types by hand, for when a photo does not work: mixed plates, poor lighting or " +
            "food already eaten. What the patient types counts as confirmed from the start. The patient must say " +
            "whether the meal was in the plan; a meal outside the plan is marked as such and still counts towards " +
            "the day. If the app sends its own identifier for the entry, sending the same meal again returns the " +
            "entry already saved.")]
    [SwaggerResponse(StatusCodes.Status201Created, "The entry was logged.", typeof(DiaryEntryResource))]
    [SwaggerResponse(StatusCodes.Status400BadRequest,
        "The time is missing, or it was not said whether the meal is in the plan.", typeof(ProblemDetails))]
    [SwaggerResponse(StatusCodes.Status401Unauthorized, "Authentication is required.")]
    [SwaggerResponse(StatusCodes.Status403Forbidden, "Only the patient writes their own diary.",
        typeof(ProblemDetails))]
    [SwaggerResponse(StatusCodes.Status409Conflict,
        "The identifier the app sent for the entry belongs to another patient's entry.",
        typeof(ProblemDetails))]
    [SwaggerResponse(StatusCodes.Status422UnprocessableEntity,
        "The food could not be resolved, or the moment falls outside the retroactive window.",
        typeof(ProblemDetails))]
    public async Task<IActionResult> LogMealManually([FromBody] LogMealManuallyResource resource)
    {
        var patientId = this.GetAuthenticatedUserId();
        if (resource.PatientId != patientId) return PatientWriteOnly();

        var result = await commandService.Handle(
            LogMealManuallyCommandAssembler.ToCommand(patientId, resource), HttpContext.RequestAborted);

        return IntakeActionResultAssembler.ToDiaryEntryResult(result, localizer,
            StatusCodes.Status201Created, await FoodNameOf(result));
    }

    [HttpPost("manual-logs/batch")]
    [Consumes("application/json")]
    [SwaggerOperation(
        Summary = "Log a meal of several foods",
        Description =
            "Logs one meal made of several foods (from 1 to 10), for example a meal idea the patient chose. Each " +
            "food is saved as its own confirmed entry, all with the same time and grouped so the diary shows them " +
            "as one meal. Everything is saved together: if one food cannot be found in the catalog, nothing is " +
            "saved. A meal that comes from a suggested idea counts as in the plan unless the patient says " +
            "otherwise; in any other case the patient must say whether it was in the plan. The day is evaluated " +
            "once for the whole meal. Sending again a meal whose foods were already saved returns the same entries.")]
    [SwaggerResponse(StatusCodes.Status201Created, "The meal was logged.", typeof(MealGroupLogResource))]
    [SwaggerResponse(StatusCodes.Status400BadRequest,
        "The time is missing, there are not 1 to 10 foods with positive portions, the origin is unknown, or it " +
        "was not said whether the meal is in the plan.",
        typeof(ProblemDetails))]
    [SwaggerResponse(StatusCodes.Status401Unauthorized, "Authentication is required.")]
    [SwaggerResponse(StatusCodes.Status403Forbidden, "Only the patient writes their own diary.",
        typeof(ProblemDetails))]
    [SwaggerResponse(StatusCodes.Status409Conflict,
        "The identifiers the app sent for the foods match another meal, or another patient's entries.",
        typeof(ProblemDetails))]
    [SwaggerResponse(StatusCodes.Status422UnprocessableEntity,
        "A food could not be resolved, or the moment falls outside the retroactive window.",
        typeof(ProblemDetails))]
    [SwaggerResponse(StatusCodes.Status500InternalServerError, "Unexpected server error.", typeof(ProblemDetails))]
    public async Task<IActionResult> LogMealGroup([FromBody] LogMealGroupResource resource)
    {
        var patientId = this.GetAuthenticatedUserId();
        if (resource.PatientId is not null && resource.PatientId != patientId) return PatientWriteOnly();

        var result = await commandService.Handle(LogMealGroupCommandAssembler.ToCommand(patientId, resource),
            HttpContext.RequestAborted);

        IReadOnlyDictionary<int, string> names = new Dictionary<int, string>();
        if (result is Result<MealGroupLogOutcome, IntakeError>.Success success)
            names = await queryService.Handle(new GetFoodNamesQuery(success.Value.Entries
                .Select(e => e.ConfirmedReferenceFoodId ?? 0).ToList()), HttpContext.RequestAborted);

        return IntakeActionResultAssembler.ToMealGroupResult(result, names, localizer);
    }

    [HttpPost("off-plan-logs")]
    [Consumes("application/json")]
    [Obsolete("Deprecated by IN-1: use planAdherence=OffPlan on manual-logs or photo-logs.")]
    [SwaggerOperation(
        Summary = "Log an off-plan meal (deprecated)",
        Description =
            "Deprecated: log the meal by hand or from a photo and mark it as outside the plan instead, so it has a " +
            "food and a portion and counts towards the day. It still works the same way for older versions of the " +
            "app. Records with one tap that the patient ate something outside the plan, and nothing else: no food, " +
            "no portion and no reason. Nothing is calculated from it and nothing is penalised, because declaring " +
            "should be easier than leaving it out.")]
    [SwaggerResponse(StatusCodes.Status201Created, "The entry was logged.", typeof(DiaryEntryResource))]
    [SwaggerResponse(StatusCodes.Status400BadRequest, "The moment is missing.", typeof(ProblemDetails))]
    [SwaggerResponse(StatusCodes.Status401Unauthorized, "Authentication is required.")]
    [SwaggerResponse(StatusCodes.Status403Forbidden, "Only the patient writes their own diary.",
        typeof(ProblemDetails))]
    [SwaggerResponse(StatusCodes.Status422UnprocessableEntity,
        "The moment falls outside the retroactive window.", typeof(ProblemDetails))]
    public async Task<IActionResult> LogOffPlanMeal([FromBody] LogOffPlanMealResource resource)
    {
        var patientId = this.GetAuthenticatedUserId();
        if (resource.PatientId != patientId) return PatientWriteOnly();

        var result = await commandService.Handle(
            LogOffPlanMealCommandAssembler.ToCommand(patientId, resource), HttpContext.RequestAborted);

        return IntakeActionResultAssembler.ToDiaryEntryResult(result, localizer,
            StatusCodes.Status201Created);
    }

    [HttpPost("{diaryEntryId:int}/estimate-confirmation")]
    [SwaggerOperation(
        Summary = "Confirm the proposed estimate",
        Description =
            "The patient accepts the proposed estimate as it is and says whether the meal was in the plan. The " +
            "proposal is kept beside the confirmation, so both what was estimated and what the patient said can be " +
            "read.")]
    [SwaggerResponse(StatusCodes.Status200OK, "The estimate was confirmed.", typeof(DiaryEntryResource))]
    [SwaggerResponse(StatusCodes.Status400BadRequest, "It was not said whether the meal is in the plan.",
        typeof(ProblemDetails))]
    [SwaggerResponse(StatusCodes.Status401Unauthorized, "Authentication is required.")]
    [SwaggerResponse(StatusCodes.Status403Forbidden, "The entry belongs to another patient.",
        typeof(ProblemDetails))]
    [SwaggerResponse(StatusCodes.Status404NotFound, "No diary entry was found.", typeof(ProblemDetails))]
    [SwaggerResponse(StatusCodes.Status409Conflict, "The entry has already been confirmed.",
        typeof(ProblemDetails))]
    [SwaggerResponse(StatusCodes.Status422UnprocessableEntity, "There is no proposal to confirm.",
        typeof(ProblemDetails))]
    public async Task<IActionResult> ConfirmEstimate(int diaryEntryId,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] EstimateConfirmationResource? resource)
    {
        var result = await commandService.Handle(
            ConfirmEstimateCommandAssembler.ToCommand(diaryEntryId, this.GetAuthenticatedUserId(), resource),
            HttpContext.RequestAborted);

        return IntakeActionResultAssembler.ToDiaryEntryResult(result, localizer,
            foodName: await FoodNameOf(result));
    }

    [HttpPost("{diaryEntryId:int}/estimate-adjustment")]
    [Consumes("application/json")]
    [SwaggerOperation(
        Summary = "Adjust the proposed estimate",
        Description =
            "The patient corrects the proposed estimate and says whether the meal was in the plan. The correction " +
            "is saved beside the proposal, never over it, so the size of the correction stays visible.")]
    [SwaggerResponse(StatusCodes.Status200OK, "The estimate was adjusted.", typeof(DiaryEntryResource))]
    [SwaggerResponse(StatusCodes.Status400BadRequest, "It was not said whether the meal is in the plan.",
        typeof(ProblemDetails))]
    [SwaggerResponse(StatusCodes.Status401Unauthorized, "Authentication is required.")]
    [SwaggerResponse(StatusCodes.Status403Forbidden, "The entry belongs to another patient.",
        typeof(ProblemDetails))]
    [SwaggerResponse(StatusCodes.Status404NotFound, "No diary entry was found.", typeof(ProblemDetails))]
    [SwaggerResponse(StatusCodes.Status409Conflict, "The entry has already been confirmed.",
        typeof(ProblemDetails))]
    [SwaggerResponse(StatusCodes.Status422UnprocessableEntity,
        "There is no proposal to adjust, or the food could not be resolved.", typeof(ProblemDetails))]
    public async Task<IActionResult> AdjustEstimate(int diaryEntryId,
        [FromBody] AdjustEstimateResource resource)
    {
        var result = await commandService.Handle(
            AdjustEstimateCommandAssembler.ToCommand(diaryEntryId, this.GetAuthenticatedUserId(),
                resource), HttpContext.RequestAborted);

        return IntakeActionResultAssembler.ToDiaryEntryResult(result, localizer,
            foodName: await FoodNameOf(result));
    }

    [HttpPost("synchronization")]
    [Consumes("application/json")]
    [SwaggerOperation(
        Summary = "Synchronise the entries queued offline",
        Description =
            "Sends the meals the app saved while it was offline. Each meal carries an identifier from the app, so a " +
            "meal sent twice is recognised and never duplicated. The server never changes the time the patient " +
            "logged a meal at. Each meal is processed and reported on its own: one that cannot be accepted does not " +
            "stop the rest. Each meal can say whether it was already confirmed and whether it was in the plan; " +
            "meals from older versions of the app without that information are still accepted.")]
    [SwaggerResponse(StatusCodes.Status200OK, "The batch was reconciled, item by item.",
        typeof(SyncOutcomeResource))]
    [SwaggerResponse(StatusCodes.Status401Unauthorized, "Authentication is required.")]
    [SwaggerResponse(StatusCodes.Status403Forbidden, "Only the patient synchronises their own queue.",
        typeof(ProblemDetails))]
    public async Task<IActionResult> SyncPendingEntries([FromBody] SyncPendingEntriesResource resource)
    {
        var patientId = this.GetAuthenticatedUserId();
        if (resource.PatientId != patientId) return PatientWriteOnly();

        var result = await commandService.Handle(
            SyncPendingEntriesCommandAssembler.ToCommand(patientId, resource),
            HttpContext.RequestAborted);

        return IntakeActionResultAssembler.ToSyncResult(result, localizer);
    }

    /// <summary>IN-1. The name of the food a just-written entry shows, or null.</summary>
    private async Task<string?> FoodNameOf(Result<DiaryEntry, IntakeError> result)
    {
        if (result is not Result<DiaryEntry, IntakeError>.Success success) return null;
        if (DiaryEntryResourceAssembler.DisplayedFoodId(success.Value) is not { } foodId) return null;

        var names = await queryService.Handle(new GetFoodNamesQuery([foodId]), HttpContext.RequestAborted);
        return names.GetValueOrDefault(foodId);
    }

    private IActionResult PatientWriteOnly()
    {
        return IntakeActionResultAssembler.ToNotFoundResult(IntakeError.PatientWriteOnly, localizer);
    }
}
