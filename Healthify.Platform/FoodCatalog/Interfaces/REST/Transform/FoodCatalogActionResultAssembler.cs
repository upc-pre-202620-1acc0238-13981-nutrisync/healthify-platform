using Healthify.Platform.FoodCatalog.Application.Internal;
using Healthify.Platform.FoodCatalog.Domain.Model.Aggregates;
using Healthify.Platform.FoodCatalog.Domain.Model.Errors;
using Healthify.Platform.FoodCatalog.Resources;
using Healthify.Platform.Shared.Application.Patterns;
using Healthify.Platform.Shared.Interfaces.REST.ProblemDetails;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;

namespace Healthify.Platform.FoodCatalog.Interfaces.REST.Transform;

/// <summary>The single place where a <see cref="FoodCatalogError" /> becomes an HTTP status.</summary>
public static class FoodCatalogActionResultAssembler
{
    public static IActionResult ToReferenceFoodResult(
        Result<ReferenceFood, FoodCatalogError> result,
        IStringLocalizer<FoodCatalogMessages> localizer,
        int successStatus = StatusCodes.Status200OK)
    {
        return result switch
        {
            Result<ReferenceFood, FoodCatalogError>.Success s =>
                new ObjectResult(ReferenceFoodResourceAssembler.ToResource(s.Value))
                    { StatusCode = successStatus },
            Result<ReferenceFood, FoodCatalogError>.Failure f => FailureResult(f.Error, localizer),
            _ => FailureResult(FoodCatalogError.UnexpectedError, localizer)
        };
    }

    public static IActionResult ToReferenceFoodListResult(
        Result<IReadOnlyList<ReferenceFood>, FoodCatalogError> result,
        IStringLocalizer<FoodCatalogMessages> localizer)
    {
        return result switch
        {
            Result<IReadOnlyList<ReferenceFood>, FoodCatalogError>.Success s =>
                new OkObjectResult(s.Value.Select(ReferenceFoodResourceAssembler.ToResource)),
            Result<IReadOnlyList<ReferenceFood>, FoodCatalogError>.Failure f =>
                FailureResult(f.Error, localizer),
            _ => FailureResult(FoodCatalogError.UnexpectedError, localizer)
        };
    }

    public static IActionResult ToCatalogImportResult(
        Result<CatalogImportSummary, FoodCatalogError> result,
        IStringLocalizer<FoodCatalogMessages> localizer,
        int successStatus = StatusCodes.Status202Accepted)
    {
        return result switch
        {
            Result<CatalogImportSummary, FoodCatalogError>.Success s =>
                new ObjectResult(CatalogImportSummaryResourceAssembler.ToResource(s.Value))
                    { StatusCode = successStatus },
            Result<CatalogImportSummary, FoodCatalogError>.Failure f => FailureResult(f.Error, localizer),
            _ => FailureResult(FoodCatalogError.UnexpectedError, localizer)
        };
    }

    /// <summary>Not-found response for the read endpoints, which do not return a Result.</summary>
    public static IActionResult ToNotFoundResult(FoodCatalogError error,
        IStringLocalizer<FoodCatalogMessages> localizer)
    {
        return FailureResult(error, localizer);
    }

    private static ObjectResult FailureResult(FoodCatalogError error,
        IStringLocalizer<FoodCatalogMessages> localizer)
    {
        return (error switch
        {
            FoodCatalogError.ReferenceFoodNotFound => Problem(StatusCodes.Status404NotFound,
                localizer["NotFoundTitle"].Value, localizer["ReferenceFoodNotFound"].Value),

            FoodCatalogError.PractitionerOnly => Problem(StatusCodes.Status403Forbidden,
                localizer["ForbiddenTitle"].Value, localizer["PractitionerOnly"].Value),

            FoodCatalogError.DuplicatedLocalOverride => Problem(StatusCodes.Status409Conflict,
                localizer["ConflictTitle"].Value, localizer["DuplicatedLocalOverride"].Value),

            FoodCatalogError.LocalNameAndNutrientsRequired => Problem(StatusCodes.Status400BadRequest,
                localizer["BadRequestTitle"].Value, localizer["LocalNameAndNutrientsRequired"].Value),
            FoodCatalogError.SourceHashRequired => Problem(StatusCodes.Status400BadRequest,
                localizer["BadRequestTitle"].Value, localizer["SourceHashRequired"].Value),
            FoodCatalogError.ExternalIdNotAllowed => Problem(StatusCodes.Status400BadRequest,
                localizer["BadRequestTitle"].Value, localizer["ExternalIdNotAllowed"].Value),
            // IN-7. Reached through the ACL only (Intake maps it to PhotoNotRecognized); mapped for completeness.
            FoodCatalogError.InconsistentNutrients => Problem(StatusCodes.Status400BadRequest,
                localizer["BadRequestTitle"].Value, localizer["InconsistentNutrients"].Value),
            FoodCatalogError.AiGenerationRequired => Problem(StatusCodes.Status400BadRequest,
                localizer["BadRequestTitle"].Value, localizer["AiGenerationRequired"].Value),

            // The request is well formed; the upstream record simply could not be expressed in this
            // vocabulary, which is a translation outcome and not a client mistake.
            FoodCatalogError.TaxonomyTranslationFailed => Problem(
                StatusCodes.Status422UnprocessableEntity,
                localizer["UnprocessableEntityTitle"].Value, localizer["TaxonomyTranslationFailed"].Value),

            // Nothing is wrong with the request and nothing is wrong with this service: the external
            // catalog is not answering, and the caller may retry later.
            FoodCatalogError.ExternalCatalogUnavailable => Problem(
                StatusCodes.Status503ServiceUnavailable,
                localizer["ServiceUnavailableTitle"].Value, localizer["ExternalCatalogUnavailable"].Value),

            _ => Problem(StatusCodes.Status500InternalServerError,
                localizer["UnexpectedServerError"].Value, localizer["UnexpectedError"].Value)
        }).WithErrorCode(error);
    }

    private static ObjectResult Problem(int status, string title, string detail)
    {
        return new ObjectResult(ProblemDetailsFactory.Create(status, title, detail)) { StatusCode = status };
    }
}
