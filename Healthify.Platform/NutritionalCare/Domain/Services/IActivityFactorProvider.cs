using Healthify.Platform.NutritionalCare.Domain.Model.ValueObjects;

namespace Healthify.Platform.NutritionalCare.Domain.Services;

/// <summary>
///     The activity factor that multiplies the BMR for each <see cref="ActivityLevel" />. The
///     defaults live in the value object; configuration may override them. Read by the target proposal.
/// </summary>
public interface IActivityFactorProvider
{
    decimal FactorFor(ActivityLevel level);
}
