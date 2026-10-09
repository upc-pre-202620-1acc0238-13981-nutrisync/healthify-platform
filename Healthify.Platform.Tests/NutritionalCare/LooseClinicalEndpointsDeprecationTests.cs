using System.Reflection;
using Healthify.Platform.NutritionalCare.Interfaces.REST;
using Microsoft.AspNetCore.Mvc.Routing;
using Swashbuckle.AspNetCore.Annotations;

namespace Healthify.Platform.Tests.NutritionalCare;

/// <summary>
///     X-1. The loose clinical endpoints the new app no longer uses are deprecated in favour of the guided
///     consultation: <c>[Obsolete]</c> (Swashbuckle publishes <c>deprecated: true</c>) and a note in the description
///     naming the consultation step that replaces each one. Same routes and verbs; their behaviour is unchanged and
///     stays covered by the command service tests (they keep their 409).
/// </summary>
public class LooseClinicalEndpointsDeprecationTests
{
    public static TheoryData<Type, string, string, string> Deprecated => new()
    {
        { typeof(NutritionalAssessmentsController), "RecordAssessment", "POST", "" },
        { typeof(NutritionalAssessmentsController), "TakeClinicalMeasurement", "POST", "{assessmentId:int}/clinical-measurements" },
        { typeof(NutritionalAssessmentsController), "CloseAssessment", "POST", "{assessmentId:int}/closure" },
        { typeof(NutritionalDiagnosesController), "IssueDiagnosis", "POST", "" },
        { typeof(NutritionPlansController), "ProposeTargets", "POST", "target-proposals" },
        { typeof(NutritionPlansController), "PrescribeTargets", "POST", "{planId:int}/prescribed-targets" },
        { typeof(NutritionPlansController), "PublishNutritionPlan", "POST", "{planId:int}/publication" }
    };

    [Theory]
    [MemberData(nameof(Deprecated))]
    public void Each_loose_endpoint_is_deprecated_and_names_the_consultation_step(Type controller, string action,
        string verb, string template)
    {
        var method = controller.GetMethod(action, BindingFlags.Public | BindingFlags.Instance)!;

        var obsolete = method.GetCustomAttribute<ObsoleteAttribute>();
        Assert.NotNull(obsolete);
        Assert.StartsWith("Deprecated by X-1", obsolete.Message);
        Assert.Contains("/consultations/", obsolete.Message);
        Assert.False(obsolete.IsError);

        var operation = method.GetCustomAttribute<SwaggerOperationAttribute>()!;
        Assert.EndsWith("(deprecated)", operation.Summary);
        Assert.StartsWith("Deprecated: the app uses", operation.Description);
        Assert.Contains("guided consultation", operation.Description);

        // Still served where it was.
        var route = Assert.Single(method.GetCustomAttributes<HttpMethodAttribute>());
        Assert.Equal([verb], route.HttpMethods);
        Assert.Equal(template, route.Template ?? "");
    }

    [Theory]
    [InlineData(typeof(NutritionalAssessmentsController), "GetAssessmentById")]
    [InlineData(typeof(NutritionPlansController), "AdjustNutritionPlan")]
    public void Endpoints_outside_the_list_are_not_deprecated(Type controller, string action)
    {
        Assert.Null(controller.GetMethod(action)!.GetCustomAttribute<ObsoleteAttribute>());
    }
}
