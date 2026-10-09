namespace Healthify.Platform.NutritionalCare.Domain.Model.Commands;

/// <summary>
///     Subflow 3.3 - Propose Targets. Clinical phase 3, first step.
/// </summary>
/// <remarks>
///     The practitioner acts before the calculation, not only after it. The five decisions that move
///     the result most arrive here and the aggregate chooses none of them: the equation, the kind and
///     value of the reference weight, the activity factor, the size of the deficit and the protein
///     target. Everything else is published arithmetic.
/// </remarks>
public record ProposeTargetsCommand(
    int PatientId,
    int PractitionerId,
    string Equation,
    string ReferenceWeightKind,
    decimal ReferenceWeightKg,
    decimal ActivityFactor,
    string DeficitKind,
    decimal DeficitValue,
    decimal ProteinGramsPerKg,
    decimal FatPercentOfEnergy);
