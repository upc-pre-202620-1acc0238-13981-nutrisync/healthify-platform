using Healthify.Platform.FoodCatalog.Domain.Model.Errors;
using Healthify.Platform.FoodCatalog.Interfaces.REST.Transform;
using Healthify.Platform.FoodCatalog.Resources;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Localization;
using Xunit;
using static Healthify.Platform.Tests.TestSupport.ProblemResponses;

namespace Healthify.Platform.Tests.FoodCatalog;

/// <summary>X-3. Every FoodCatalog problem carries <c>extensions.code</c>; status, title and detail do not change.</summary>
public class ErrorCodeExtensionTests
{
    private readonly IStringLocalizer<FoodCatalogMessages> _localizer = EchoLocalizer<FoodCatalogMessages>();

    public static TheoryData<FoodCatalogError> Errors => AllValues<FoodCatalogError>();

    [Fact]
    public void A_duplicated_override_is_a_409_and_the_external_catalog_down_a_503_with_their_codes()
    {
        var duplicated = Problem(FoodCatalogActionResultAssembler.ToNotFoundResult(
            FoodCatalogError.DuplicatedLocalOverride, _localizer));
        var unavailable = Problem(FoodCatalogActionResultAssembler.ToNotFoundResult(
            FoodCatalogError.ExternalCatalogUnavailable, _localizer));

        Assert.Equal(StatusCodes.Status409Conflict, duplicated.Status);
        Assert.Equal("DuplicatedLocalOverride", Code(duplicated));
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, unavailable.Status);
        Assert.Equal("ExternalCatalogUnavailable", Code(unavailable));
    }

    [Theory]
    [MemberData(nameof(Errors))]
    public void Every_error_carries_its_enum_name(FoodCatalogError error)
    {
        Assert.Equal(ExpectedCode(error),
            Code(Problem(FoodCatalogActionResultAssembler.ToNotFoundResult(error, _localizer))));
    }
}
