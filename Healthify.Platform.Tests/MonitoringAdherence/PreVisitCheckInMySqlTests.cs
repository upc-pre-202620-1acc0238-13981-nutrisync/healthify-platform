using Healthify.Platform.MonitoringAdherence.Domain.Model.Aggregates;
using Healthify.Platform.MonitoringAdherence.Domain.Model.Commands;
using Healthify.Platform.MonitoringAdherence.Infrastructure.Persistence.EFC.Repositories;
using Healthify.Platform.Shared.Infrastructure.Persistence.EFC.Repositories;
using Healthify.Platform.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace Healthify.Platform.Tests.MonitoringAdherence;

/// <summary>
///     MA-4 against a real MySQL (Category=MySql; skipped without HEALTHIFY_IT_MYSQL): the questions stored as JSON
///     come back as value objects with their origin, an edit is noticed by the value comparers, and the unique
///     index keeps one check in per visit.
/// </summary>
[Collection(MySqlCollection.Name)]
public class PreVisitCheckInMySqlTests
{
    [MySqlFact]
    [Trait("Category", "MySql")]
    public async Task A_check_in_round_trips_is_edited_and_stays_one_per_visit()
    {
        await using var db = await MySqlIntegrationDatabase.CreateAsync(DateOnly.FromDateTime(DateTime.UtcNow));
        var now = DateTimeOffset.UtcNow;
        int followUpId;
        await using (var context = db.NewContext())
        {
            var visit = new ScheduledFollowUp(new ScheduleFollowUpCommand(10, 20, now.AddDays(3), ["Fasting"]));
            context.Add(visit);
            await context.SaveChangesAsync();
            followUpId = visit.Id.Value;

            var checkIn = new PreVisitCheckIn(new SubmitPreVisitCheckInCommand(followUpId, 10, "Hard",
                ["Dinners", "Weekends"],
                [
                    new PreVisitCheckInQuestionInput("¿Puedo comer fuera los viernes?"),
                    new PreVisitCheckInQuestionInput("¿Cómo armo cenas con más proteína?", "AiSuggested", 42)
                ]), visit, now);
            await new PreVisitCheckInRepository(context).AddAsync(checkIn);
            await new UnitOfWork(context).CompleteAsync();
        }

        await using (var context = db.NewContext())
        {
            var repository = new PreVisitCheckInRepository(context);
            var stored = await repository.FindByFollowUpIdAsync(followUpId);
            Assert.NotNull(stored);
            Assert.Equal(["Dinners", "Weekends"], stored.Difficulties.Select(d => d.Value));
            Assert.Equal([("Patient", (long?)null), ("AiSuggested", (long?)42)],
                stored.Questions.Select(q => (q.Origin, q.AiGenerationId)));

            var visit = await new ScheduledFollowUpRepository(context).FindByIdAsync(followUpId);
            stored.Edit(new SubmitPreVisitCheckInCommand(followUpId, 10, "Good", ["Cravings"],
                [new PreVisitCheckInQuestionInput("¿Puedo cambiar el desayuno?")]), visit!, now.AddHours(1));
            repository.Update(stored);
            await new UnitOfWork(context).CompleteAsync();
        }

        await using (var context = db.NewContext())
        {
            var edited = await new PreVisitCheckInRepository(context).FindByFollowUpIdAsync(followUpId);
            Assert.NotNull(edited);
            Assert.Equal("Good", edited.Feeling.Value);
            Assert.Equal(["Cravings"], edited.Difficulties.Select(d => d.Value));
            Assert.Equal("¿Puedo cambiar el desayuno?", Assert.Single(edited.Questions).Text);
            Assert.NotNull(edited.EditedAt);

            var visit = await new ScheduledFollowUpRepository(context).FindByIdAsync(followUpId);
            context.Add(new PreVisitCheckIn(new SubmitPreVisitCheckInCommand(followUpId, 10, "Fair"), visit!, now));
            await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        }
    }

    [MySqlFact]
    [Trait("Category", "MySql")]
    public async Task An_ai_generation_id_beyond_the_int_range_round_trips_and_legacy_rows_still_read()
    {
        await using var db = await MySqlIntegrationDatabase.CreateAsync(DateOnly.FromDateTime(DateTime.UtcNow));
        var now = DateTimeOffset.UtcNow;
        const long generationId = 5_000_000_000L;
        int bigId, legacyId;
        await using (var context = db.NewContext())
        {
            var first = new ScheduledFollowUp(new ScheduleFollowUpCommand(10, 20, now.AddDays(3)));
            var second = new ScheduledFollowUp(new ScheduleFollowUpCommand(11, 20, now.AddDays(3)));
            context.AddRange(first, second);
            await context.SaveChangesAsync();

            var big = new PreVisitCheckIn(new SubmitPreVisitCheckInCommand(first.Id.Value, 10, "Good", null,
                [new PreVisitCheckInQuestionInput("¿Cómo armo cenas con más proteína?", "AiSuggested",
                    generationId)]), first, now);
            var legacy = new PreVisitCheckIn(new SubmitPreVisitCheckInCommand(second.Id.Value, 11, "Fair"), second,
                now);
            context.AddRange(big, legacy);
            await context.SaveChangesAsync();
            (bigId, legacyId) = (first.Id.Value, second.Id.Value);

            // A row as the int version wrote it: the same JSON shape, a whole number.
            await context.Database.ExecuteSqlRawAsync(
                "UPDATE pre_visit_check_ins SET questions = CAST({0} AS JSON) WHERE follow_up_id = {1}",
                """[{"Text":"¿Puedo cenar más tarde?","Origin":"AiSuggested","AiGenerationId":42}]""", legacyId);
        }

        await using (var context = db.NewContext())
        {
            var repository = new PreVisitCheckInRepository(context);
            Assert.Equal(generationId,
                Assert.Single((await repository.FindByFollowUpIdAsync(bigId))!.Questions).AiGenerationId);
            Assert.Equal(42L,
                Assert.Single((await repository.FindByFollowUpIdAsync(legacyId))!.Questions).AiGenerationId);
        }
    }
}
