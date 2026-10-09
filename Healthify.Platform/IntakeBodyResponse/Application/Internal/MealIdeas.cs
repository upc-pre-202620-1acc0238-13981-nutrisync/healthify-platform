using Healthify.Platform.IntakeBodyResponse.Domain.Model.Errors;
using Healthify.Platform.IntakeBodyResponse.Domain.Model.ValueObjects;
using Healthify.Platform.IntakeBodyResponse.Domain.Services;
using Healthify.Platform.Shared.Application.Ai;

namespace Healthify.Platform.IntakeBodyResponse.Application.Internal;

/// <summary>
///     IA-3. Why the meal ideas did not come: a business error of this context (<see cref="IntakeError" />: nothing
///     left today, no published targets…) or a technical error of the shared AI module (<see cref="AiError" />),
///     which the controller maps with <c>IntakeActionResultAssembler.ToAiFailureResult</c>. Exactly one is set.
/// </summary>
/// <remarks>
///     Same shape as <c>MonitoringAiFailure</c> (DECISIÓN IA-2), so <c>AiError</c> keeps its own texts and statuses
///     without copying its values into the enum of this context.
/// </remarks>
public sealed record IntakeAiFailure
{
    private IntakeAiFailure(IntakeError? error, AiError? aiError)
    {
        Error = error;
        AiError = aiError;
    }

    public IntakeError? Error { get; }
    public AiError? AiError { get; }

    public static IntakeAiFailure Of(IntakeError error)
    {
        return new IntakeAiFailure(error, null);
    }

    public static IntakeAiFailure Of(AiError error)
    {
        return new IntakeAiFailure(null, error);
    }

    public override string ToString()
    {
        return Error?.ToString() ?? AiError?.ToString() ?? "None";
    }
}

/// <summary>IA-3. What the model answers (schema of <c>meal-ideas@n.md</c>).</summary>
public sealed record MealIdeasOutput(IReadOnlyList<MealIdeaOutput> Ideas);

/// <summary>IA-3. One idea as the model wrote it, before the catalog checks its figures.</summary>
public sealed record MealIdeaOutput(
    string Name,
    decimal EnergyKcal,
    decimal ProteinG,
    decimal CarbG,
    decimal FatG,
    IReadOnlyList<MealIdeaIngredientOutput> Ingredients,
    string Why);

/// <summary>IA-3. One ingredient as the model wrote it.</summary>
public sealed record MealIdeaIngredientOutput(string Name, decimal Grams);

/// <summary>IA-3. Application DTO: the ideas for what is left today, and what they were computed against.</summary>
/// <param name="LocalDate">The patient's day.</param>
/// <param name="Remaining">What is left of today's targets, computed without AI.</param>
/// <param name="Restrictions">The restriction codes of the plan every idea respects.</param>
/// <param name="Ideas">Two or three ideas.</param>
/// <param name="AiGenerationId">The generation they come from.</param>
/// <param name="GeneratedAt">When they were generated (a cached answer keeps its time).</param>
public sealed record MealIdeasView(
    DateOnly LocalDate,
    RemainingTargets Remaining,
    IReadOnlyList<string> Restrictions,
    IReadOnlyList<MealIdea> Ideas,
    long AiGenerationId,
    DateTimeOffset GeneratedAt);

/// <summary>
///     IA-3, guard 6. Runs <see cref="MealIdeaRules.Screen" /> over the ideas as the model wrote them and rejects the
///     output when fewer than <see cref="MealIdeaRules.MinimumIdeas" /> pass: the command service retries once.
///     Ideas that fail alone are discarded later, when the catalog has recalculated their figures.
/// </summary>
public sealed class MealIdeasOutputValidator(
    RemainingTargets remaining,
    IReadOnlyCollection<string> restrictions,
    IRestrictionLexicon lexicon) : IAiOutputValidator<MealIdeasOutput>
{
    public IReadOnlyList<string> Validate(MealIdeasOutput output)
    {
        var reasons = new List<string>();
        var valid = 0;
        foreach (var idea in output.Ideas ?? [])
        {
            var screened = MealIdeaRules.Screen(idea.Name, idea.EnergyKcal,
                (idea.Ingredients ?? []).Select(i => i.Name), idea.Why, remaining, restrictions, lexicon);
            if (screened.Count == 0) valid++;
            reasons.AddRange(screened);
        }

        if (valid >= MealIdeaRules.MinimumIdeas) return [];

        reasons.Add($"$.ideas: {valid} valid idea(s), at least {MealIdeaRules.MinimumIdeas} expected.");
        return reasons;
    }
}
