using Healthify.Platform.Shared.Interfaces.REST.ProblemDetails;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using NSubstitute;
using Xunit;

namespace Healthify.Platform.Tests.TestSupport;

/// <summary>X-3. Readers over the problem responses of the action result assemblers.</summary>
public static class ProblemResponses
{
    /// <summary>A localizer that answers every key with the key itself, so a test can see which text was picked.</summary>
    public static IStringLocalizer<T> EchoLocalizer<T>()
    {
        var localizer = Substitute.For<IStringLocalizer<T>>();
        localizer[Arg.Any<string>()].Returns(call => new LocalizedString(call.Arg<string>(), call.Arg<string>()));
        return localizer;
    }

    /// <summary>The problem of an error response, asserting it is one.</summary>
    public static ProblemDetails Problem(IActionResult result)
    {
        return Assert.IsAssignableFrom<ProblemDetails>(Assert.IsType<ObjectResult>(result).Value);
    }

    /// <summary>The value of <c>extensions.code</c>, or null when absent.</summary>
    public static string? Code(ProblemDetails problem)
    {
        return problem.Extensions.TryGetValue(ProblemDetailsErrorCodes.ExtensionKey, out var code)
            ? code as string
            : null;
    }

    /// <summary>The code every value of an error enum must carry: its own name, and InternalError for UnexpectedError.</summary>
    public static string ExpectedCode<TError>(TError error) where TError : struct, Enum
    {
        return error.ToString() == "UnexpectedError" ? ProblemDetailsErrorCodes.InternalError : error.ToString();
    }

    /// <summary>Every value of an error enum, as xUnit theory data.</summary>
    public static TheoryData<TError> AllValues<TError>() where TError : struct, Enum
    {
        var data = new TheoryData<TError>();
        foreach (var value in Enum.GetValues<TError>()) data.Add(value);
        return data;
    }
}
