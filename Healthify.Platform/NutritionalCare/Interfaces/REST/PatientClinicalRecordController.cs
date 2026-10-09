using Healthify.Platform.CareRelationship.Interfaces.Acl;
using Healthify.Platform.NutritionalCare.Application.QueryServices;
using Healthify.Platform.NutritionalCare.Domain.Model.Errors;
using Healthify.Platform.NutritionalCare.Domain.Model.Queries;
using Healthify.Platform.NutritionalCare.Interfaces.REST.Resources;
using Healthify.Platform.NutritionalCare.Interfaces.REST.Transform;
using Healthify.Platform.NutritionalCare.Resources;
using Healthify.Platform.Shared.Interfaces.REST.Extensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Swashbuckle.AspNetCore.Annotations;
using ProblemDetails = Microsoft.AspNetCore.Mvc.ProblemDetails;

namespace Healthify.Platform.NutritionalCare.Interfaces.REST;

/// <summary>
///     The per-patient read models of the Nutritional Care bounded context: Assessment Timeline,
///     Active Diagnosis, Active Plan and Plan Version History.
/// </summary>
/// <remarks>
///     Practitioner only, without exception. These responses carry the diagnosis, its rationale and
///     the calculation basis, which are professional information: the patient receives the result of
///     the clinical act, never the procedure.
/// </remarks>
[ApiController]
[Route("api/v1/patients")]
[Authorize(Roles = "Practitioner")]
[Tags("Nutritional Care")]
[Produces("application/json")]
public class PatientClinicalRecordController(
    INutritionalAssessmentQueryService assessmentQueryService,
    INutritionalDiagnosisQueryService diagnosisQueryService,
    INutritionPlanQueryService planQueryService,
    ICareRelationshipContextFacade careRelationshipContextFacade,
    IStringLocalizer<NutritionalCareMessages> localizer) : ControllerBase
{
    [HttpGet("{patientId:int}/nutritional-assessments")]
    [SwaggerOperation(
        Summary = "List the assessments of a patient",
        Description =
            "Lists the patient's assessments. Corrections appear as new assessments that refer to the ones they " +
            "replace, because a closed assessment is never edited.")]
    [SwaggerResponse(StatusCodes.Status200OK, "The assessments, most recent first.",
        typeof(IEnumerable<NutritionalAssessmentResource>))]
    [SwaggerResponse(StatusCodes.Status401Unauthorized, "Authentication is required.")]
    [SwaggerResponse(StatusCodes.Status403Forbidden, "No active care link with this patient.")]
    public async Task<IActionResult> GetAssessments(int patientId)
    {
        if (!await IsLinkedToAsync(patientId)) return Forbid();

        var assessments = await assessmentQueryService.Handle(
            new GetAssessmentsByPatientIdQuery(patientId), HttpContext.RequestAborted);

        return Ok(assessments.Select(NutritionalAssessmentResourceAssembler.ToResource));
    }

    [HttpGet("{patientId:int}/nutritional-diagnoses/active")]
    [SwaggerOperation(
        Summary = "Get the active diagnosis of a patient",
        Description =
            "Returns the patient's current diagnosis. Only the practitioner can see it: the patient never sees " +
            "their diagnosis in the app.")]
    [SwaggerResponse(StatusCodes.Status200OK, "The active diagnosis.",
        typeof(NutritionalDiagnosisResource))]
    [SwaggerResponse(StatusCodes.Status401Unauthorized, "Authentication is required.")]
    [SwaggerResponse(StatusCodes.Status403Forbidden, "No active care link with this patient.")]
    [SwaggerResponse(StatusCodes.Status404NotFound, "This patient has no active diagnosis.",
        typeof(ProblemDetails))]
    public async Task<IActionResult> GetActiveDiagnosis(int patientId)
    {
        if (!await IsLinkedToAsync(patientId)) return Forbid();

        var diagnosis = await diagnosisQueryService.Handle(
            new GetActiveDiagnosisByPatientIdQuery(patientId), HttpContext.RequestAborted);

        if (diagnosis is null)
            return NutritionalCareActionResultAssembler.ToNotFoundResult(
                NutritionalCareError.DiagnosisNotFound, localizer);

        return Ok(NutritionalDiagnosisResourceAssembler.ToResource(diagnosis));
    }

    [HttpGet("{patientId:int}/nutrition-plans")]
    [SwaggerOperation(
        Summary = "List the plan versions of a patient",
        Description =
            "Lists every version of the patient's plan, including the replaced ones with their change reasons. " +
            "Nothing in this history is ever deleted.")]
    [SwaggerResponse(StatusCodes.Status200OK, "The versions, most recent first.",
        typeof(IEnumerable<NutritionPlanResource>))]
    [SwaggerResponse(StatusCodes.Status401Unauthorized, "Authentication is required.")]
    [SwaggerResponse(StatusCodes.Status403Forbidden, "No active care link with this patient.")]
    public async Task<IActionResult> GetPlans(int patientId)
    {
        if (!await IsLinkedToAsync(patientId)) return Forbid();

        var plans = await planQueryService.Handle(
            new GetPlansByPatientIdQuery(patientId), HttpContext.RequestAborted);

        return Ok(plans.Select(NutritionPlanResourceAssembler.ToResource));
    }

    [HttpGet("{patientId:int}/nutrition-plans/active")]
    [SwaggerOperation(
        Summary = "Get the active plan of a patient",
        Description =
            "Returns the patient's active plan, with how its numbers were calculated. A patient has one active plan " +
            "version at a time.")]
    [SwaggerResponse(StatusCodes.Status200OK, "The active plan version.", typeof(NutritionPlanResource))]
    [SwaggerResponse(StatusCodes.Status401Unauthorized, "Authentication is required.")]
    [SwaggerResponse(StatusCodes.Status403Forbidden, "No active care link with this patient.")]
    [SwaggerResponse(StatusCodes.Status404NotFound, "This patient has no active plan.",
        typeof(ProblemDetails))]
    public async Task<IActionResult> GetActivePlan(int patientId)
    {
        if (!await IsLinkedToAsync(patientId)) return Forbid();

        var plan = await planQueryService.Handle(
            new GetActivePlanByPatientIdQuery(patientId), HttpContext.RequestAborted);

        if (plan is null)
            return NutritionalCareActionResultAssembler.ToNotFoundResult(
                NutritionalCareError.PlanNotFound, localizer);

        return Ok(NutritionPlanResourceAssembler.ToResource(plan));
    }

    /// <summary>
    ///     Asks the Open Host Service of Care Relationship whether this practitioner may see this
    ///     patient. It degrades to false, so an unanswered question denies access.
    /// </summary>
    private async Task<bool> IsLinkedToAsync(int patientId)
    {
        return await careRelationshipContextFacade.IsCareLinkActive(
            patientId, this.GetAuthenticatedUserId(), HttpContext.RequestAborted);
    }
}
