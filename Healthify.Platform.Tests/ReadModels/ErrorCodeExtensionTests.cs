using Healthify.Platform.ReadModels.Interfaces.REST.Transform;
using Healthify.Platform.Shared.Resources;
using Microsoft.AspNetCore.Http;
using Xunit;
using static Healthify.Platform.Tests.TestSupport.ProblemResponses;

namespace Healthify.Platform.Tests.ReadModels;

/// <summary>X-3. The one refusal of the composite reads carries <c>extensions.code</c>.</summary>
public class ErrorCodeExtensionTests
{
    [Fact]
    public void The_ownership_refusal_is_a_403_access_not_allowed()
    {
        var problem = Problem(ReadModelActionResultAssembler.ToForbiddenResult(EchoLocalizer<SharedResource>()));

        Assert.Equal(StatusCodes.Status403Forbidden, problem.Status);
        Assert.Equal("AccessNotAllowed", Code(problem));
        Assert.Equal("Forbidden", problem.Title);
        Assert.Equal("AccessNotAllowed", problem.Detail);
    }
}
