using Microsoft.AspNetCore.Mvc;

namespace Healthify.Platform.Shared.Infrastructure.Interfaces.ASP.Configuration.Extensions;

public static class MvcOptionsExtensions
{
    public static void AddKebabCaseRouteNamingConvention(this MvcOptions options)
    {
        options.Conventions.Add(new KebabCaseRouteNamingConvention());
    }
}
