namespace Healthify.Platform.NutritionalCare.Domain.Model.Commands;

/// <summary>
///     NC-10 - Open the scheduled rechecks whose date arrived (DECISIÓN §12-#12). Issued by the clock, never by a
///     user: each one is a new <c>ScheduledRecheck</c> item in the inbox, and it can never modify a plan.
/// </summary>
/// <param name="Now">The moment of the cycle.</param>
/// <param name="BatchSize">How many due rechecks one cycle opens at most.</param>
public record OpenDueRechecksCommand(DateTimeOffset Now, int BatchSize = 200);
