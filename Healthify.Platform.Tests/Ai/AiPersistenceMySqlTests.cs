using Healthify.Platform.CareRelationship.Domain.Model.Aggregates;
using Healthify.Platform.CareRelationship.Domain.Model.Commands;
using Healthify.Platform.CareRelationship.Infrastructure.Persistence.EFC.Repositories;
using Healthify.Platform.Shared.Application.Ai;
using Healthify.Platform.Shared.Infrastructure.Ai.Persistence;
using Healthify.Platform.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Healthify.Platform.Tests.Ai;

/// <summary>
///     IA-0, CR-2 and IA-1 against a real MySQL (Category=MySql; skipped without HEALTHIFY_IT_MYSQL): the audit
///     table with its quota count and both purges, the AI columns of the consent (and their default on a row written
///     without them), and the preferences keyed by patient.
/// </summary>
[Collection(MySqlCollection.Name)]
[Trait("Category", "MySql")]
public class AiPersistenceMySqlTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 6, 15, 0, 0, TimeSpan.Zero);

    [MySqlFact]
    public async Task The_audit_records_counts_and_purges_by_patient_feature_and_expiry()
    {
        await using var db = await MySqlIntegrationDatabase.CreateAsync(DateOnly.FromDateTime(DateTime.UtcNow));
        await using var scope = db.NewScope();
        var log = new EfAiGenerationLog(scope.ServiceProvider.GetRequiredService<IServiceScopeFactory>());

        var id = await log.RecordAsync(Row(AiFeature.WeeklySummary, 10, AiGenerationStatus.Succeeded, Now,
            """{"headline":"Buena semana"}"""));
        await log.RecordAsync(Row(AiFeature.WeeklySummary, 10, AiGenerationStatus.Blocked, Now));
        await log.RecordAsync(Row(AiFeature.MealIdeas, 10, AiGenerationStatus.Rejected, Now.AddHours(-30)));
        await log.RecordAsync(Row(AiFeature.DiagnosisSuggestion, 11, AiGenerationStatus.Failed, Now, requestedBy: 20,
            expiresAt: Now.AddMinutes(-1)));

        Assert.True(id > 0);
        Assert.Equal(1, await log.CountSinceAsync(AiFeature.WeeklySummary, 10, null, Now.AddDays(-1)));
        Assert.Equal(0, await log.CountSinceAsync(AiFeature.MealIdeas, 10, null, Now.AddDays(-1)));
        Assert.Equal(1, await log.CountSinceAsync(AiFeature.DiagnosisSuggestion, 99, 20, Now.AddDays(-1)));

        await using (var context = db.NewContext())
        {
            var stored = await context.Set<AiGeneration>().SingleAsync(g => g.Id == id);
            Assert.Equal("""{"headline": "Buena semana"}""", stored.OutputJson); // MySQL normalizes json
            Assert.Equal(new string('a', 64), stored.InputHash);
            Assert.Equal(Now, stored.CreatedAt);
        }

        Assert.Equal(1, await log.PurgeExpiredAsync(Now));
        Assert.Equal(1, await log.PurgeForPatientAsync(10, AiFeature.MealIdeas));
        Assert.Equal(2, await log.PurgeForPatientAsync(10));
        await using (var context = db.NewContext())
            Assert.Equal(0, await context.Set<AiGeneration>().CountAsync());
    }

    [MySqlFact]
    public async Task The_ai_consent_columns_and_the_preferences_round_trip()
    {
        await using var db = await MySqlIntegrationDatabase.CreateAsync(DateOnly.FromDateTime(DateTime.UtcNow));
        await using (var context = db.NewContext())
        {
            // A link written without the CR-2 columns, as every link before it was.
            await context.Database.ExecuteSqlRawAsync(
                "INSERT INTO care_links (patient_id, practitioner_id, established_at, consent_granted, consent_scope, " +
                "consent_granted_at) VALUES (30, 20, UTC_TIMESTAMP(), 1, 'diary', UTC_TIMESTAMP())");
            var link = new CareLink(new EstablishCareLinkCommand(10, 20, 1));
            link.GrantConsent(new GrantConsentCommand(0, 10, "diary", true));
            context.Add(link);
            var preferences = new AiPreferences(10);
            preferences.Change(true, false, true, true);
            context.Add(preferences);
            await context.SaveChangesAsync();
        }

        await using (var context = db.NewContext())
        {
            var links = new CareLinkRepository(context);
            var legacy = await links.FindActiveByPatientIdAsync(30);
            Assert.False(legacy!.ConsentAiProcessingGranted);
            Assert.Null(legacy.ConsentAiProcessingDecidedAt);

            var link = await links.FindActiveByPatientIdAsync(10);
            Assert.True(link!.HasAiProcessingConsent);
            Assert.NotNull(link.ConsentAiProcessingDecidedAt);
            link.WithdrawConsent();
            await context.SaveChangesAsync();

            var preferences = await new AiPreferencesRepository(context).FindByPatientIdAsync(10);
            Assert.Equal((true, false, true), (preferences!.WeeklySummaryEnabled, preferences.MealIdeasEnabled,
                preferences.SuggestedQuestionsEnabled));
            Assert.NotNull(preferences.CreatedAt);
        }

        await using (var context = db.NewContext())
        {
            var withdrawn = await context.Set<CareLink>().SingleAsync(c => c.PatientId == 10);
            Assert.False(withdrawn.ConsentAiProcessingGranted);
            Assert.Equal(withdrawn.ConsentWithdrawnAt, withdrawn.ConsentAiProcessingDecidedAt);
        }
    }

    private static AiGenerationRecord Row(AiFeature feature, int patientId, AiGenerationStatus status,
        DateTimeOffset at, string? output = null, int? requestedBy = null, DateTimeOffset? expiresAt = null)
    {
        return new AiGenerationRecord(feature.Name, patientId, requestedBy, $"{feature.PromptName}@1",
            "gemini-3.5-flash", new string('a', 64), output, status, null, 100, 20, 850, at,
            expiresAt ?? at.AddDays(180));
    }
}
