namespace Healthify.Platform.NutritionalCare.Domain.Model.Commands;

/// <summary>
///     NC-10 - Recover the plan proposals the in-memory queue lost (a restart) or never got: queue the generation for
///     the open sustained deviations without a proposal, opened in the last <paramref name="MaxAge" />, whose patient
///     consented to AI processing. Issued by the clock (at start-up and every N minutes), never by a user.
/// </summary>
/// <param name="Now">The moment of the cycle.</param>
/// <param name="MaxAge">How far back an item is still worth a proposal (72 h by default).</param>
/// <param name="BatchSize">How many items one cycle reads at most.</param>
public record RecoverPlanProposalsCommand(DateTimeOffset Now, TimeSpan MaxAge, int BatchSize = 200);
