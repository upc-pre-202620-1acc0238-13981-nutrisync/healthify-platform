using Healthify.Platform.IntakeBodyResponse.Domain.Model.Aggregates;
using Healthify.Platform.IntakeBodyResponse.Domain.Model.ValueObjects;
using Healthify.Platform.IntakeBodyResponse.Infrastructure.Persistence.EFC.Repositories;
using Healthify.Platform.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace Healthify.Platform.Tests.IntakeBodyResponse;

/// <summary>
///     The diary over a range of days against a real MySQL (Category=MySql; skipped without HEALTHIFY_IT_MYSQL):
///     filtered in the database on the declared wall clock, both ends inclusive, oldest first, one patient only,
///     with the composite index of the migration in place.
/// </summary>
[Collection(MySqlCollection.Name)]
[Trait("Category", "MySql")]
public class DiaryRangeMySqlTests
{
    private const int PatientId = 10;
    private const int OtherPatientId = 11;

    private static readonly TimeSpan Lima = TimeSpan.FromHours(-5);

    [MySqlFact]
    public async Task A_range_of_days_is_read_on_the_declared_wall_clock_for_one_patient()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var monday = today.AddDays(-3);
        var tuesday = monday.AddDays(1);
        await using var db = await MySqlIntegrationDatabase.CreateAsync(today);

        await using (var context = db.NewContext())
        {
            // Monday 23:30 and Wednesday 00:30 sit just outside Tuesday; Tuesday 21:00 in Lima is Wednesday in UTC.
            context.Add(Entry(PatientId, monday, 23, 30));
            context.Add(Entry(PatientId, tuesday, 21, 0));
            context.Add(Entry(PatientId, tuesday, 8, 0));
            context.Add(Entry(PatientId, tuesday.AddDays(1), 0, 30));
            context.Add(Entry(OtherPatientId, tuesday, 12, 0));
            await context.SaveChangesAsync();
        }

        await using (var context = db.NewContext())
        {
            var repository = new DiaryEntryRepository(context);

            var oneDay = (await repository.ListByPatientIdBetweenAsync(PatientId, tuesday, tuesday)).ToList();
            Assert.Equal([8, 21], oneDay.Select(e => e.LocalTimestamp.Hour));
            Assert.All(oneDay, e => Assert.Equal(tuesday, e.LocalDate));

            var threeDays = await repository.ListByPatientIdBetweenAsync(PatientId, monday, tuesday.AddDays(1));
            Assert.Equal([monday, tuesday, tuesday, tuesday.AddDays(1)], threeDays.Select(e => e.LocalDate));

            Assert.Empty(await repository.ListByPatientIdBetweenAsync(PatientId, today, today));

            var indexes = await context.Database.SqlQuery<string>(
                    $"SELECT DISTINCT index_name AS Value FROM information_schema.statistics WHERE table_schema = DATABASE() AND table_name = 'diary_entries' AND index_name = 'ix_diary_entries_patient_id_local_timestamp'")
                .ToListAsync();
            Assert.Single(indexes);
        }
    }

    private static DiaryEntry Entry(int patientId, DateOnly day, int hour, int minute)
    {
        var moment = new DateTimeOffset(day.ToDateTime(new TimeOnly(hour, minute)), Lima);
        var entry = new DiaryEntry(patientId, new LocalTimestamp(moment), new Provenance(Provenance.Manual),
            new SyncState(SyncState.Synced));
        entry.ConfirmDirectly(new ConfirmedEstimate(231, 150m, DateTimeOffset.UtcNow),
            new PlanAdherence(PlanAdherence.InPlan));
        return entry;
    }
}
