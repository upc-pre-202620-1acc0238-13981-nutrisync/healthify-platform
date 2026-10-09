using Healthify.Platform.Shared.Interfaces.REST.ProblemDetails;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static Healthify.Platform.Tests.TestSupport.ProblemResponses;
using MvcProblemDetailsFactory = Microsoft.AspNetCore.Mvc.Infrastructure.ProblemDetailsFactory;

namespace Healthify.Platform.Tests.Shared;

/// <summary>X-3. The codes the framework responses get from Shared: validation, 5xx and the bearer challenge.</summary>
public class ProblemDetailsErrorCodesTests
{
    [Fact]
    public void Model_validation_problems_get_validation_failed_and_keep_their_errors()
    {
        var factory = MvcFactory();
        var modelState = new ModelStateDictionary();
        modelState.AddModelError("Email", "The Email field is required.");

        var problem = factory.CreateValidationProblemDetails(new DefaultHttpContext(), modelState);

        Assert.Equal(StatusCodes.Status400BadRequest, problem.Status);
        Assert.Equal("ValidationFailed", Code(problem));
        Assert.Equal(["The Email field is required."], problem.Errors["Email"]);
    }

    [Fact]
    public void Unhandled_server_errors_get_internal_error_and_no_internals()
    {
        var problem = MvcFactory().CreateProblemDetails(new DefaultHttpContext(),
            StatusCodes.Status500InternalServerError);

        Assert.Equal("InternalError", Code(problem));
        Assert.Single(problem.Extensions, e => e.Key == ProblemDetailsErrorCodes.ExtensionKey);
        Assert.DoesNotContain(problem.Extensions.Keys, k => k is "exception" or "stackTrace");
    }

    [Fact]
    public void A_code_already_set_by_an_assembler_is_never_overwritten()
    {
        var problem = ProblemDetailsFactory.Create(StatusCodes.Status503ServiceUnavailable, "t", "d",
            code: "AiProviderUnavailable");

        ProblemDetailsErrorCodes.ApplyDefault(problem);

        Assert.Equal("AiProviderUnavailable", Code(problem));
    }

    [Fact]
    public void A_4xx_without_a_known_cause_gets_no_invented_code()
    {
        var problem = new ProblemDetails { Status = StatusCodes.Status404NotFound };

        ProblemDetailsErrorCodes.ApplyDefault(problem);

        Assert.Null(Code(problem));
    }

    [Fact]
    public void The_bearer_challenge_problem_is_authentication_required_and_keeps_its_shape()
    {
        var problem = ProblemDetailsFactory.Create(StatusCodes.Status401Unauthorized, "Unauthorized",
            "AuthenticationRequired", "/api/v1/users/7", ProblemDetailsErrorCodes.AuthenticationRequired);

        Assert.Equal("AuthenticationRequired", Code(problem));
        Assert.Equal("https://tools.ietf.org/html/rfc7235#section-3.1", problem.Type);
        Assert.Equal("/api/v1/users/7", problem.Instance);
    }

    [Fact]
    public void Without_a_code_the_factory_adds_no_extension()
    {
        var problem = ProblemDetailsFactory.Create(StatusCodes.Status409Conflict, "t", "d");

        Assert.Empty(problem.Extensions);
    }

    private static MvcProblemDetailsFactory MvcFactory()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddControllers();
        // Same hook as Program.cs (section 4, ProblemDetails).
        services.AddProblemDetails(options =>
            options.CustomizeProblemDetails = context => ProblemDetailsErrorCodes.ApplyDefault(context.ProblemDetails));
        return services.BuildServiceProvider().GetRequiredService<MvcProblemDetailsFactory>();
    }
}
