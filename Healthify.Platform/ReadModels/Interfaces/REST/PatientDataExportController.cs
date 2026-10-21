using Healthify.Platform.ReadModels.Application;
using Healthify.Platform.ReadModels.Interfaces.REST.Transform;
using Healthify.Platform.Shared.Interfaces.REST.Extensions;
using Healthify.Platform.Shared.Resources;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Swashbuckle.AspNetCore.Annotations;
using ProblemDetails = Microsoft.AspNetCore.Mvc.ProblemDetails;

namespace Healthify.Platform.ReadModels.Interfaces.REST;

/// <summary>
///     Data export. "Descargar mis datos" (Settings): the patient downloads a copy of their own data.
/// </summary>
/// <remarks>
///     Only the patient themselves: identity from the token, and no practitioner path, not even with an active link.
///     Like every composite endpoint it injects the composer and nothing else, and it returns only what the patient
///     can see (no diagnosis, rationale, calculation basis, BMI or BMI category).
/// </remarks>
[ApiController]
[Route("api/v1/patients")]
[Authorize(Roles = "Patient")]
[Tags("Read Models")]
public class PatientDataExportController(
    PatientDataExportComposer composer,
    IStringLocalizer<SharedResource> localizer) : ControllerBase
{
    [HttpGet("{patientId:int}/data-export")]
    [SwaggerOperation(
        Summary = "Download a copy of the patient's own data",
        Description =
            "Returns a ZIP file (healthify-mis-datos-<yyyy-MM-dd>.zip) with one CSV per table: perfil.csv (name, " +
            "email, language, registration date), diario.csv (food diary with grams, energy and macronutrients, " +
            "whether it was in the plan and its source), autopesajes.csv (home weigh-ins, fasting and protocol), " +
            "metas.csv (plan versions with energy, macronutrients, date in force and the change reason as a code and " +
            "a sentence), consultas.csv (visits with date, modality and status) and vinculos.csv (nutritionists, " +
            "link status and consent dates). CSV files are UTF-8 with BOM, comma separated, escaped as RFC 4180, " +
            "with ISO 8601 dates and a decimal point; headers are in the patient's preferred language. No " +
            "diagnosis, clinical rationale, calculation basis, body mass index or its category is included. Only " +
            "the patient themselves may download it.")]
    [SwaggerResponse(StatusCodes.Status200OK, "The ZIP archive with the CSV files.", typeof(FileContentResult),
        "application/zip")]
    [SwaggerResponse(StatusCodes.Status401Unauthorized, "Authentication is required.")]
    [SwaggerResponse(StatusCodes.Status403Forbidden,
        "The session is not this patient's (or is not a patient's).", typeof(ProblemDetails))]
    public async Task<IActionResult> GetDataExport(int patientId)
    {
        if (patientId != this.GetAuthenticatedUserId())
            return ReadModelActionResultAssembler.ToForbiddenResult(localizer);

        var file = await composer.Export(patientId, HttpContext.RequestAborted);
        return File(file.Content, "application/zip", file.FileName);
    }
}
