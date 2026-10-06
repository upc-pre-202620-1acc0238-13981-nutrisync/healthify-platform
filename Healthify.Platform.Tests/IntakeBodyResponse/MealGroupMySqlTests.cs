using Healthify.Platform.IntakeBodyResponse.Domain.Model.Aggregates;
using Healthify.Platform.IntakeBodyResponse.Domain.Model.ValueObjects;
using Healthify.Platform.IntakeBodyResponse.Infrastructure.Persistence.EFC.Repositories;
using Healthify.Platform.Tests.TestSupport;

namespace Healthify.Platform.Tests.IntakeBodyResponse;

/// <summary>
///     IN-6 against a real MySQL (Category=MySql; skipped without HEALTHIFY_IT_MYSQL): <c>meal_group_id</c> and
///     <c>origin</c> round-trip, and an entry logged alone keeps both null.
/// </summary>
[Collection(MySqlCollection.Name)]
[Trait("Category", "MySql")]
public class MealGroupMySqlTests
{
    private const int PatientId = 10;

    [MySqlFact]
    public async Task A_meal_group_round_trips_and_a_single_entry_has_no_group()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        await using var db = await MySqlIntegrationDatabase.CreateAsync(today);
        var groupId = Guid.NewGuid();
        var moment = new DateTimeOffset(
            DateTime.SpecifyKind(DateTime.UtcNow.Date.AddHours(13), DateTimeKind.Unspecified), TimeSpan.FromHours(-5));

        await using (var context = db.NewContext())
        {
            foreach (var (food, grams) in new[] { (231, 150m), (3, 120m) })
            {
                var entry = new DiaryEntry(PatientId, new LocalTimestamp(moment), new Provenance(Provenance.Manual),
                    new SyncState(SyncState.Synced));
                entry.ConfirmDirectly(new ConfirmedEstimate(food, grams, DateTimeOffset.UtcNow),
                    new PlanAdherence(PlanAdherence.InPlan));
                entry.JoinMealGroup(groupId, new EntryOrigin(EntryOrigin.MealIdea));
                context.Add(entry);
            }

            var alone = new DiaryEntry(PatientId, new LocalTimestamp(moment.AddHours(3)),
                new Provenance(Provenance.Manual), new SyncState(SyncState.Synced));
            alone.ConfirmDirectly(new ConfirmedEstimate(9, 100m, DateTimeOffset.UtcNow),
                new PlanAdherence(PlanAdherence.OffPlan));
            context.Add(alone);
            await context.SaveChangesAsync();
        }

        await using (var context = db.NewContext())
        {
            var entries = (await new DiaryEntryRepository(context).ListByPatientIdAsync(PatientId,
                DateOnly.FromDateTime(moment.Date))).OrderBy(e => e.Id.Value).ToList();

            Assert.Equal(3, entries.Count);
            Assert.All(entries.Take(2), e =>
            {
                Assert.Equal(groupId, e.MealGroupId);
                Assert.Equal(EntryOrigin.MealIdea, e.Origin?.Value);
            });
            Assert.Null(entries[2].MealGroupId);
            Assert.Null(entries[2].Origin);
        }
    }
}
