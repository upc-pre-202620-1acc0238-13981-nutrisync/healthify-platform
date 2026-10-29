using Healthify.Platform.NutritionalCare.Domain.Model.Commands;
using Healthify.Platform.NutritionalCare.Domain.Model.ValueObjects;

namespace Healthify.Platform.NutritionalCare.Domain.Services;

/// <summary>
///     The parameters step 3 calculates with before the practitioner changes any, from the baseline
///     and today's measurement. Defaults in code, overridable in
///     <c>NutritionalCare:DefaultTargetParameters</c>.
/// </summary>
public interface IDefaultTargetParametersPolicy
{
    /// <summary>
    ///     A complete <see cref="ProposeTargetsCommand" />: the configured equation, deficit, protein and fat,
    ///     the activity factor of <paramref name="activityLevel" /> and the measured weight as reference.
    /// </summary>
    ProposeTargetsCommand For(int patientId, int practitionerId, DiagnosisCode? diagnosis,
        ActivityLevel activityLevel, decimal measuredWeightKg);
}
