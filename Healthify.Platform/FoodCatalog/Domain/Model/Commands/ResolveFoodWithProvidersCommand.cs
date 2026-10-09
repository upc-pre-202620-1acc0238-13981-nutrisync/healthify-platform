namespace Healthify.Platform.FoodCatalog.Domain.Model.Commands;

/// <summary>
///     IN-7 - the second step of resolving a recognized dish: Search Food (Subflow 6.3) with the name, which tops up
///     the local catalog from the external providers (cached through Subflow 6.2, idempotent by source hash), and then
///     the entry the name stands for, chosen as FC-2 chooses.
/// </summary>
/// <param name="Name">The dish as the AI named it.</param>
public record ResolveFoodWithProvidersCommand(string Name);
