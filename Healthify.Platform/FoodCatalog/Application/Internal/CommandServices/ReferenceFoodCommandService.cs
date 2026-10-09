using Cortex.Mediator;
using Healthify.Platform.FoodCatalog.Application.CommandServices;
using Healthify.Platform.FoodCatalog.Application.Internal;
using Healthify.Platform.FoodCatalog.Domain.Model.Aggregates;
using Healthify.Platform.FoodCatalog.Domain.Model.Commands;
using Healthify.Platform.FoodCatalog.Domain.Model.Errors;
using Healthify.Platform.FoodCatalog.Domain.Model.Events;
using Healthify.Platform.FoodCatalog.Domain.Model.ValueObjects;
using Healthify.Platform.FoodCatalog.Domain.Repositories;
using Healthify.Platform.FoodCatalog.Domain.Services;
using Healthify.Platform.Shared.Application.Patterns;
using Healthify.Platform.Shared.Domain.Repositories;

namespace Healthify.Platform.FoodCatalog.Application.Internal.CommandServices;

/// <summary>
///     The four commands of the Food Catalog bounded context.
/// </summary>
/// <remarks>
///     The interesting decisions here are all about the edge. Import translates and announces but
///     writes nothing, so that the only path into the catalog is the caching policy of Subflow 6.2
///     and every stored row has demonstrably been through the translation. Search reads the local
///     catalog first and treats the external providers as a top-up that is allowed to fail.
/// </remarks>
public class ReferenceFoodCommandService(
    IReferenceFoodRepository referenceFoodRepository,
    IUnitOfWork unitOfWork,
    IEnumerable<IExternalFoodCatalogProvider> externalProviders,
    ILogger<ReferenceFoodCommandService> logger,
    IMediator mediator,
    IConfiguration? configuration = null) : IReferenceFoodCommandService
{
    private const int MaxRecordsPerProvider = 100;

    /// <summary>IN-7. Significant words of a recognized dish searched when looking for a similar entry.</summary>
    private const int SimilarSearchWords = 4;

    /// <summary>Subflow 6.1 - Import Catalog Snapshot.</summary>
    public async Task<Result<CatalogImportSummary, FoodCatalogError>> Handle(
        ImportCatalogSnapshotCommand command, CancellationToken cancellationToken = default)
    {
        var term = (command.Term ?? string.Empty).Trim();
        if (term.Length == 0)
            return new Result<CatalogImportSummary, FoodCatalogError>.Failure(
                FoodCatalogError.TaxonomyTranslationFailed);

        var max = Math.Clamp(command.Max <= 0 ? 25 : command.Max, 1, MaxRecordsPerProvider);

        try
        {
            var providers = externalProviders.ToList();
            if (providers.Count == 0)
                return new Result<CatalogImportSummary, FoodCatalogError>.Failure(
                    FoodCatalogError.ExternalCatalogUnavailable);

            var translatedTotal = 0;
            var failedTotal = 0;

            foreach (var provider in providers)
            {
                if (cancellationToken.IsCancellationRequested) break;

                var snapshot = await provider.FetchSnapshotAsync(term, max, cancellationToken);

                // Business rule: Translation Failed is a negative branch, not an exception (6.1).
                // A record that cannot be expressed in this vocabulary is dropped and announced; the
                // import carries on with the ones that did translate.
                foreach (var reason in snapshot.TranslationFailures)
                {
                    failedTotal++;
                    logger.LogWarning("A record from {Provider} was not translated: {Reason}",
                        snapshot.ProviderName, reason);
                    await mediator.PublishAsync(new TranslationFailed(snapshot.ProviderName, reason),
                        cancellationToken);
                }

                foreach (var food in snapshot.TranslatedFoods)
                {
                    translatedTotal++;
                    await mediator.PublishAsync(ToTranslatedEvent(food), cancellationToken);
                }

                await mediator.PublishAsync(
                    new ExternalCatalogSnapshotImported(snapshot.ProviderName, term,
                        snapshot.TranslatedFoods.Count, snapshot.TranslationFailures.Count),
                    cancellationToken);
            }

            // Every provider answered with nothing but failures: the catalog is unreachable rather
            // than empty, and the caller deserves to know the difference.
            if (translatedTotal == 0 && failedTotal > 0)
                return new Result<CatalogImportSummary, FoodCatalogError>.Failure(
                    FoodCatalogError.ExternalCatalogUnavailable);

            return new Result<CatalogImportSummary, FoodCatalogError>.Success(
                new CatalogImportSummary(term, providers.Count, translatedTotal, failedTotal));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The catalog import for term {Term} did not complete", term);
            return new Result<CatalogImportSummary, FoodCatalogError>.Failure(
                FoodCatalogError.UnexpectedError);
        }
    }

    /// <summary>Subflow 6.2 - Cache Food Locally. The only path into the catalog.</summary>
    public async Task<Result<ReferenceFood, FoodCatalogError>> Handle(CacheFoodLocallyCommand command,
        CancellationToken cancellationToken = default)
    {
        // Business rule: No External Id Enters The Domain (Food Catalog, Subflow 6.1). Checked here
        // and not only at the adapter, because this is the doorway every stored row goes through. A
        // name that is nothing but digits is a barcode or a provider key that survived a bad
        // translation, and it must not become the name of a food somebody logs as a meal.
        if (LooksLikeAnExternalIdentifier(command.LocalName))
            return Failure(FoodCatalogError.ExternalIdNotAllowed);

        LocalName localName;
        NutrientsPer100g nutrients;
        try
        {
            localName = new LocalName(command.LocalName);
            nutrients = new NutrientsPer100g(command.EnergyKcalPer100g, command.ProteinGPer100g,
                command.CarbGPer100g, command.FatGPer100g);
        }
        catch (ArgumentException)
        {
            // Business rule: Taxonomy Translation Mandatory (Subflow 6.1).
            return Failure(FoodCatalogError.TaxonomyTranslationFailed);
        }

        SourceHash sourceHash;
        try
        {
            sourceHash = new SourceHash(command.SourceHash);
        }
        catch (ArgumentException)
        {
            // Business rule: Source Hash Stored For Upstream Changes (Subflow 6.1).
            return Failure(FoodCatalogError.SourceHashRequired);
        }

        try
        {
            // Idempotency, which is what makes both the scheduled import and the seeder safe to run
            // again: the upstream fingerprint identifies the row, so a second import of an unchanged
            // record writes nothing and announces nothing.
            var existing = await referenceFoodRepository.FindBySourceHashAsync(sourceHash,
                cancellationToken);

            if (existing is not null)
            {
                // IN-7: neither a local override nor an AI-estimated food follows an upstream record.
                if (existing.IsProtectedFromImport) return Success(existing);

                existing.RefreshFromUpstream(localName, nutrients);
                referenceFoodRepository.Update(existing);
                await unitOfWork.CompleteAsync(cancellationToken);
                return Success(existing);
            }

            var referenceFood = new ReferenceFood(localName, nutrients, sourceHash);

            await referenceFoodRepository.AddAsync(referenceFood, cancellationToken);
            await unitOfWork.CompleteAsync(cancellationToken);

            await mediator.PublishAsync(
                new ReferenceFoodCached(referenceFood.Id.Value, referenceFood.LocalNameText),
                cancellationToken);

            return Success(referenceFood);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not cache a translated reference food");
            return Failure(FoodCatalogError.UnexpectedError);
        }
    }

    /// <summary>Subflow 6.3 - Search Food.</summary>
    /// <remarks>
    ///     Business rule: Search Falls Back To Local Cache Offline (Subflow 6.3). Read from the
    ///     server side, that rule is local-first: the local catalog answers, and the external
    ///     providers are consulted only to top up a thin result. A provider that is unreachable
    ///     therefore degrades the search instead of breaking it.
    ///     TODO: the offline half of this rule belongs to the client. The device keeps its own copy of
    ///     the Local Food Catalog and answers from it with no server at all; what is implemented here
    ///     is the server-side half. See hotspot "local cache size" below.
    /// </remarks>
    public async Task<Result<IReadOnlyList<ReferenceFood>, FoodCatalogError>> Handle(
        SearchFoodCommand command, CancellationToken cancellationToken = default)
    {
        var term = (command.Term ?? string.Empty).Trim();
        var max = Math.Clamp(command.Max <= 0 ? 25 : command.Max, 1, MaxRecordsPerProvider);

        try
        {
            var local = (await referenceFoodRepository.SearchByLocalNameAsync(term, max,
                cancellationToken)).ToList();

            var consultedExternalProviders = false;

            // TODO: hotspot (event storming 6, hotspot 2) - local cache size. The catalog currently
            // caches every food that anybody searches for, which is the "full catalog" end of the
            // open question. The alternative, caching only the N foods a given patient actually uses,
            // needs a decision on who owns that N and where the per-patient scoping lives. Assumed
            // interpretation: the conservative one for a shared server-side catalog, which is that a
            // food searched once is worth keeping for everyone.
            if (term.Length >= 3 && local.Count < max)
            {
                consultedExternalProviders = await TopUpFromExternalProvidersAsync(term, max - local.Count,
                    cancellationToken);

                if (consultedExternalProviders)
                    local = (await referenceFoodRepository.SearchByLocalNameAsync(term, max,
                        cancellationToken)).ToList();
            }

            await mediator.PublishAsync(new FoodSearchPerformed(term, local.Count,
                consultedExternalProviders), cancellationToken);

            return new Result<IReadOnlyList<ReferenceFood>, FoodCatalogError>.Success(local);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The food search for term {Term} did not complete", term);
            return new Result<IReadOnlyList<ReferenceFood>, FoodCatalogError>.Failure(
                FoodCatalogError.UnexpectedError);
        }
    }

    /// <summary>Subflow 6.4 - Create Local Override.</summary>
    public async Task<Result<ReferenceFood, FoodCatalogError>> Handle(CreateLocalOverrideCommand command,
        CancellationToken cancellationToken = default)
    {
        // Business rule: Practitioner Only (Subflow 6.4). The role itself is enforced by the
        // endpoint; this is the identity the command could not have been built without.
        if (command.PractitionerId <= 0) return Failure(FoodCatalogError.PractitionerOnly);

        LocalName localName;
        try
        {
            // Business rule: Local Name And Nutrients Required (Subflow 6.4). Both value objects
            // refuse to exist incomplete, so the check is their constructor.
            localName = new LocalName(command.LocalName);
            _ = new NutrientsPer100g(command.EnergyKcalPer100g, command.ProteinGPer100g,
                command.CarbGPer100g, command.FatGPer100g);
        }
        catch (ArgumentException)
        {
            return Failure(FoodCatalogError.LocalNameAndNutrientsRequired);
        }

        try
        {
            // MySQL has no partial indexes, so uniqueness restricted to overrides cannot be a
            // database constraint and is checked here instead.
            // TODO: two practitioners creating the same override at the same instant can both pass
            // this check. The consequence is a duplicate row in a generic catalog, not a clinical
            // error, so it is accepted rather than serialised.
            if (await referenceFoodRepository.ExistsLocalOverrideWithNameAsync(localName.Value,
                    cancellationToken))
                return Failure(FoodCatalogError.DuplicatedLocalOverride);

            var referenceFood = new ReferenceFood(command);

            await referenceFoodRepository.AddAsync(referenceFood, cancellationToken);
            await unitOfWork.CompleteAsync(cancellationToken);

            await mediator.PublishAsync(
                new LocalFoodOverrideCreated(referenceFood.Id.Value, command.PractitionerId,
                    referenceFood.LocalNameText), cancellationToken);

            return Success(referenceFood);
        }
        catch (ArgumentException)
        {
            return Failure(FoodCatalogError.LocalNameAndNutrientsRequired);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not create a local override for practitioner {PractitionerId}",
                command.PractitionerId);
            return Failure(FoodCatalogError.UnexpectedError);
        }
    }

    /// <summary>IN-7 - Create AI Estimated Food.</summary>
    /// <remarks>
    ///     The order is the specification:
    ///     1. the name (No External Id Enters The Domain, Local Name) and the nutrients (AI Estimated Nutrients
    ///     Coherent), each to its own error, and the generation it is traced to;
    ///     2. idempotency by the digest of the normalized name: an existing food is returned as it is;
    ///     3. Similar Dish Reused, Not Duplicated: a catalog entry that is clearly the same dish
    ///     (<see cref="FoodNameMatcher.ClearSimilarMatch" />) is returned instead of creating another;
    ///     4. the new food, verified automatically, saved;
    ///     5. a race with another request creating the same dish is lost gracefully: the unique index on the source
    ///     hash kept the other row, the insert that lost is detached and the winner is returned;
    ///     6. the event after the commit.
    /// </remarks>
    public async Task<Result<ReferenceFood, FoodCatalogError>> Handle(CreateAiEstimatedFoodCommand command,
        CancellationToken cancellationToken = default)
    {
        if (LooksLikeAnExternalIdentifier(command.Name)) return Failure(FoodCatalogError.ExternalIdNotAllowed);

        LocalName localName;
        try
        {
            localName = new LocalName(DisplayNameOf(command.Name));
        }
        catch (ArgumentException)
        {
            return Failure(FoodCatalogError.LocalNameAndNutrientsRequired);
        }

        NutrientsPer100g nutrients;
        try
        {
            // Business rule: AI Estimated Nutrients Coherent (IN-7)
            nutrients = new NutrientsPer100g(command.EnergyKcalPer100g, command.ProteinGPer100g,
                command.CarbGPer100g, command.FatGPer100g);
            if (!NutrientCoherence.IsCoherent(nutrients.EnergyKcal, nutrients.ProteinG, nutrients.CarbG,
                    nutrients.FatG, Tolerance))
                return Failure(FoodCatalogError.InconsistentNutrients);
        }
        catch (ArgumentException)
        {
            return Failure(FoodCatalogError.InconsistentNutrients);
        }

        if (command.AiGenerationId <= 0) return Failure(FoodCatalogError.AiGenerationRequired);

        var sourceHash = SourceHash.ForAiEstimated(localName.Value);
        try
        {
            var existing = await referenceFoodRepository.FindBySourceHashAsync(sourceHash, cancellationToken);
            if (existing is not null) return Success(existing);

            // Business rule: Similar Dish Reused, Not Duplicated (IN-7)
            var candidates = new List<ReferenceFood>();
            foreach (var term in FoodNameMatcher.SearchTermsOf(localName.Value, SimilarSearchWords))
                candidates.AddRange(await referenceFoodRepository.SearchByLocalNameAsync(term,
                    CandidatesPerResolution, cancellationToken));
            if (FoodNameMatcher.ClearSimilarMatch(localName.Value, candidates) is { } similar)
            {
                logger.LogInformation("AI estimated dish resolved to the similar catalog entry {ReferenceFoodId}",
                    similar.Id.Value);
                return Success(similar);
            }

            var referenceFood = ReferenceFood.AiEstimated(localName, nutrients, command.AiGenerationId, Tolerance);
            await referenceFoodRepository.AddAsync(referenceFood, cancellationToken);
            try
            {
                await unitOfWork.CompleteAsync(cancellationToken);
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                // Two analyses of the same dish racing each other: the unique index on the source hash kept one.
                // The insert that lost was never stored; removing it only detaches it, so the caller's next save
                // does not try it again.
                referenceFoodRepository.Remove(referenceFood);
                var winner = await referenceFoodRepository.FindBySourceHashAsync(sourceHash, cancellationToken);
                if (winner is null) throw;
                return Success(winner);
            }

            await mediator.PublishAsync(
                new AiEstimatedFoodCreated(referenceFood.Id.Value, referenceFood.LocalNameText,
                    command.AiGenerationId), cancellationToken);

            return Success(referenceFood);
        }
        catch (ArgumentException)
        {
            return Failure(FoodCatalogError.InconsistentNutrients);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not create an AI estimated reference food");
            return Failure(FoodCatalogError.UnexpectedError);
        }
    }

    /// <summary>IN-7 - the dish resolved with the external providers' help.</summary>
    /// <remarks>
    ///     Search Food (Subflow 6.3) asks the providers for the name and caches whatever translates (Subflow 6.2,
    ///     idempotent by source hash); FC-2's matcher then picks the entry the name stands for among the local
    ///     catalog, now topped up. A provider that fails degrades to the local catalog, as search always does.
    /// </remarks>
    public async Task<Result<FoodNameResolution, FoodCatalogError>> Handle(ResolveFoodWithProvidersCommand command,
        CancellationToken cancellationToken = default)
    {
        var name = (command.Name ?? string.Empty).Trim();
        if (name.Length == 0)
            return new Result<FoodNameResolution, FoodCatalogError>.Success(new FoodNameResolution(name, null));

        try
        {
            var search = await Handle(new SearchFoodCommand(name, MaxRecordsPerProvider / 4), cancellationToken);
            if (search.IsFailure)
                logger.LogWarning("The provider search for a recognized dish did not complete; local catalog only");

            var term = FoodNameMatcher.SearchTermOf(name);
            var candidates = term is null
                ? []
                : await referenceFoodRepository.SearchByLocalNameAsync(term, CandidatesPerResolution,
                    cancellationToken);
            return new Result<FoodNameResolution, FoodCatalogError>.Success(
                new FoodNameResolution(name, FoodNameMatcher.BestMatch(name, candidates)));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not resolve a recognized dish with the external providers");
            return new Result<FoodNameResolution, FoodCatalogError>.Failure(FoodCatalogError.UnexpectedError);
        }
    }

    /// <summary>
    ///     IN-7. <c>FoodCatalog:AiEstimatedNutrients:EnergyTolerancePercent</c> (15) and <c>…:EnergyToleranceKcal</c>
    ///     (20): the larger of the two is allowed between the energy and 4P + 4C + 9G.
    /// </summary>
    private NutrientTolerance Tolerance => new(
        configuration?.GetValue<decimal?>("FoodCatalog:AiEstimatedNutrients:EnergyTolerancePercent") is >= 0 and var percent
            ? percent
            : NutrientTolerance.DefaultPercent,
        configuration?.GetValue<decimal?>("FoodCatalog:AiEstimatedNutrients:EnergyToleranceKcal") is >= 0 and var kcal
            ? kcal
            : NutrientTolerance.DefaultKcal);

    /// <summary>IN-7. The name as the catalog shows it: trimmed, single spaces, first letter in upper case.</summary>
    private static string DisplayNameOf(string? name)
    {
        var compact = string.Join(' ', (name ?? string.Empty).Split(' ', '\t', '\n', '\r')
            .Where(part => part.Length > 0));
        return compact.Length == 0 ? compact : char.ToUpperInvariant(compact[0]) + compact[1..];
    }

    /// <summary>IN-7. Catalog entries read before choosing the one a recognized dish stands for (as FC-2).</summary>
    private const int CandidatesPerResolution = 50;

    /// <summary>
    ///     Asks the external providers for what the local catalog could not supply, and announces
    ///     whatever translated. The caching policy is what stores it.
    /// </summary>
    /// <returns>True when at least one provider answered with a translated record.</returns>
    private async Task<bool> TopUpFromExternalProvidersAsync(string term, int missing,
        CancellationToken cancellationToken)
    {
        var translatedAny = false;

        foreach (var provider in externalProviders)
        {
            if (cancellationToken.IsCancellationRequested) break;

            try
            {
                var snapshot = await provider.FetchSnapshotAsync(term, missing, cancellationToken);

                foreach (var food in snapshot.TranslatedFoods)
                {
                    translatedAny = true;
                    await mediator.PublishAsync(ToTranslatedEvent(food), cancellationToken);
                }
            }
            catch (Exception ex)
            {
                // The rule is that search falls back to the local cache. An unreachable provider is
                // the ordinary case that rule exists for, not an error the caller should see.
                logger.LogWarning(ex, "Provider {Provider} could not be consulted for term {Term}",
                    provider.ProviderName, term);
            }
        }

        return translatedAny;
    }

    private static ReferenceFoodTranslated ToTranslatedEvent(ExternalFoodRecord food)
    {
        return new ReferenceFoodTranslated(
            food.LocalName.Value,
            food.Nutrients.EnergyKcal,
            food.Nutrients.ProteinG,
            food.Nutrients.CarbG,
            food.Nutrients.FatG,
            food.SourceHash.Value);
    }

    /// <summary>A name that carries no letters is an upstream key, not a food anybody would recognise.</summary>
    private static bool LooksLikeAnExternalIdentifier(string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate)) return false;
        return !candidate.Any(char.IsLetter);
    }

    private static Result<ReferenceFood, FoodCatalogError> Success(ReferenceFood referenceFood)
    {
        return new Result<ReferenceFood, FoodCatalogError>.Success(referenceFood);
    }

    private static Result<ReferenceFood, FoodCatalogError> Failure(FoodCatalogError error)
    {
        return new Result<ReferenceFood, FoodCatalogError>.Failure(error);
    }
}
