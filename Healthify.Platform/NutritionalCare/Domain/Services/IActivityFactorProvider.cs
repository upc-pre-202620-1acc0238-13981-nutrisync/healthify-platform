using Healthify.Platform.NutritionalCare.Domain.Model.ValueObjects;

namespace Healthify.Platform.NutritionalCare.Domain.Services;

/// <summary>
///     NC-3. The activity factor that multiplies the BMR for each <see cref="ActivityLevel" />. The
///     defaults live in the value object; configuration may override them. Read by NC-5.
/// </summary>
public interface IActivityFactorProvider
{
    decimal FactorFor(ActivityLevel level);
}
