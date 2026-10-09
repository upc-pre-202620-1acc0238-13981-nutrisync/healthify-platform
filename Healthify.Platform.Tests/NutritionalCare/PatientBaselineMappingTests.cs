using Healthify.Platform.NutritionalCare.Domain.Model.Aggregates;
using Healthify.Platform.Shared.Infrastructure.Persistence.EFC.Configuration;
using Microsoft.EntityFrameworkCore;

namespace Healthify.Platform.Tests.NutritionalCare;

/// <summary>
///     NC-1. MySql.EntityFrameworkCore reads a DATE column as DateTime and throws when asked for a
///     DateOnly (found against a real MySQL 8.4). The mapping must keep its explicit conversion.
/// </summary>
public class PatientBaselineMappingTests
{
    [Fact]
    public void Birth_date_is_stored_as_a_date_through_an_explicit_DateTime_conversion()
    {
        // Building the model needs the provider, not a server: nothing connects.
        using var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseMySQL("server=localhost;database=model_only;user=none;password=none").Options);

        var property = context.Model.FindEntityType(typeof(PatientBaseline))!.FindProperty("BirthDate")!;
        var converter = property.GetValueConverter();

        Assert.Equal("date", property.GetColumnType());
        Assert.NotNull(converter);
        Assert.Equal(typeof(DateTime), converter.ProviderClrType);
        Assert.Equal(new DateOnly(2000, 2, 29),
            converter.ConvertFromProvider(new DateTime(2000, 2, 29, 0, 0, 0, DateTimeKind.Unspecified)));
    }
}
