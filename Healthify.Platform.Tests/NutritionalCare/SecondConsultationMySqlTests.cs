using Healthify.Platform.IntakeBodyResponse.Domain.Model.Aggregates;
using Healthify.Platform.NutritionalCare.Application.Internal;
using Healthify.Platform.NutritionalCare.Domain.Model.Aggregates;
using Healthify.Platform.NutritionalCare.Domain.Model.Commands;
using Healthify.Platform.NutritionalCare.Domain.Model.Errors;
using Healthify.Platform.NutritionalCare.Domain.Model.ValueObjects;
using Healthify.Platform.NutritionalCare.Domain.Repositories;
using Healthify.Platform.Shared.Application.Patterns;
using Healthify.Platform.Shared.Domain.Repositories;
using Healthify.Platform.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Healthify.Platform.Tests.NutritionalCare;

/// <summary>
///     NC-2/NC-7 acceptance test against a real MySQL server: the same first and second consultation as
///     <see cref="SecondConsultationInMemoryTests" />, with the real repositories, the real transaction, the
///     generated column of One Consultation In Progress Per Patient and data that is read back from the
///     database. Each test creates its own <c>healthify_it_*</c> database and drops it, pass or fail.
/// </summary>
/// <remarks>
///     Skipped unless <c>HEALTHIFY_IT_MYSQL</c> holds a server connection string (see CLAUDE.md §5):
///     <c>dotnet test --filter "Category=MySql"</c>.
/// </remarks>
[Collection(MySqlCollection.Name)]
[Trait("Category", "MySql")]
public class SecondConsultationMySqlTests
{
    private const int PatientId = 10;
    private const int PractitionerId = 20;
    private static readonly DateOnly Today = new(2026, 9, 18);

    /// <summary>(a) First and second complete consultations, with data that really persists.</summary>
    [MySqlFact]
    public async Task The_second_complete_consultation_replaces_diagnosis_and_version_in_the_database()
    {
        await using var db = await MySqlIntegrationDatabase.CreateAsync(Today);
        await SeedBaselineAsync(db);

        var first = await ConsultationFlow.CompleteAsync(db.Consultations, PatientId, PractitionerId, 80m,
            DiagnosisCode.ObesityGradeI, "first-publication");
        await AssertNeverTwoActiveAsync(db);

        var secondId = await ConsultationFlow.StartAsync(db.Consultations, PatientId, PractitionerId);
        await ConsultationFlow.MeasureAsync(db.Consultations, secondId, PractitionerId, 74.2m);
        await AssertNeverTwoActiveAsync(db);
        await ConsultationFlow.DiagnoseAsync(db.Consultations, secondId, PractitionerId,
            DiagnosisCode.OverweightGradeI);
        await AssertNeverTwoActiveAsync(db);
        await ConsultationFlow.TargetsAsync(db.Consultations, secondId, PractitionerId);
        await AssertNeverTwoActiveAsync(db);
        var second = ConsultationFlow.Ok(
            await ConsultationFlow.PublishAsync(db.Consultations, secondId, PractitionerId, "second-publication"),
            "publication");
        await AssertNeverTwoActiveAsync(db);

        await using (var context = db.NewContext())
        {
            var diagnoses = await context.Set<NutritionalDiagnosis>().Where(d => d.PatientId == PatientId)
                .ToListAsync();
            var d1 = diagnoses.Single(d => d.Id.Value == first.Diagnosis.Id.Value);
            var d2 = diagnoses.Single(d => d.Id.Value == second.Diagnosis.Id.Value);
            Assert.True(d2.IsActive);
            Assert.Null(d2.PendingConsultationId);
            Assert.False(d1.IsActive);
            Assert.NotNull(d1.SupersededAt);

            var plans = await context.Set<NutritionPlan>().Where(p => p.PatientId == PatientId).ToListAsync();
            var v1 = plans.Single(p => p.Version == 1);
            var v2 = plans.Single(p => p.Version == 2);
            Assert.True(v2.IsActive);
            Assert.False(v1.IsActive);
            Assert.NotNull(v1.SupersededAt);
            Assert.Equal("Nueva consulta del 18 sept. 2026", v2.ChangeReason!.Value);
            Assert.Equal([Guideline.ReduceSalt], v2.Guidelines.Where(g => !g.IsCustom).Select(g => g.Code));

            var cache = await context.Set<ActiveTargetsCache>().SingleAsync(c => c.PatientId == PatientId);
            Assert.Equal(2, cache.PlanVersion);

            var consultations = await context.Set<Consultation>().Where(c => c.PatientId == PatientId)
                .ToListAsync();
            Assert.All(consultations, c => Assert.Equal(ConsultationState.Completed, c.State.Value));
            Assert.Equal("second-publication",
                consultations.Single(c => c.Id.Value == secondId).IdempotencyKey);
        }

        // Repeating the publication with the same key does not create v3 and publishes nothing.
        db.Mediator.ClearReceivedCalls();
        var replay = ConsultationFlow.Ok(
            await ConsultationFlow.PublishAsync(db.Consultations, secondId, PractitionerId, "second-publication"),
            "replay");
        Assert.True(replay.Replayed);
        Assert.Equal(2, replay.Plan.Version);
        Assert.Empty(Fakes.Published(db.Mediator));
        await using (var context = db.NewContext())
        {
            Assert.Equal(2, await context.Set<NutritionPlan>().CountAsync(p => p.PatientId == PatientId));
        }
    }

    /// <summary>(b) The last save of the publication fails: the whole transaction is rolled back.</summary>
    [MySqlFact]
    public async Task A_failed_publication_rolls_everything_back_and_publishes_nothing()
    {
        await using var db = await MySqlIntegrationDatabase.CreateAsync(Today);
        await SeedBaselineAsync(db);
        var first = await ConsultationFlow.CompleteAsync(db.Consultations, PatientId, PractitionerId, 80m,
            DiagnosisCode.ObesityGradeI, "first-publication");
        var secondId = await ConsultationFlow.StartAsync(db.Consultations, PatientId, PractitionerId);
        await ConsultationFlow.MeasureAsync(db.Consultations, secondId, PractitionerId, 74.2m);
        var pending = await ConsultationFlow.DiagnoseAsync(db.Consultations, secondId, PractitionerId,
            DiagnosisCode.OverweightGradeI);
        var draft = await ConsultationFlow.TargetsAsync(db.Consultations, secondId, PractitionerId);

        db.Mediator.ClearReceivedCalls();
        db.Failure.FailOn = "UPDATE `consultations`";
        var failed = await ConsultationFlow.PublishAsync(db.Consultations, secondId, PractitionerId, "second");
        db.Failure.FailOn = null;

        Assert.Equal(NutritionalCareError.UnexpectedError,
            Assert.IsType<Result<ConsultationPublicationOutcome, NutritionalCareError>.Failure>(failed).Error);
        Assert.Empty(Fakes.Published(db.Mediator));
        await using (var context = db.NewContext())
        {
            var d1 = await context.Set<NutritionalDiagnosis>().SingleAsync(d => d.Id == first.Diagnosis.Id);
            var d2 = await context.Set<NutritionalDiagnosis>().SingleAsync(d => d.Id == pending.Id);
            Assert.True(d1.IsActive);
            Assert.True(d2.IsPendingFor(secondId));

            var v1 = await context.Set<NutritionPlan>().SingleAsync(p => p.Id == first.Plan.Id);
            var v2 = await context.Set<NutritionPlan>().SingleAsync(p => p.Id == draft.Id);
            Assert.True(v1.IsActive);
            Assert.Null(v1.SupersededAt);
            Assert.False(v2.IsPublished);
            Assert.False(v2.IsActive);
            Assert.Null(v2.ChangeReason);

            var consultation = await context.Set<Consultation>().SingleAsync(c => c.Id == new ConsultationId(secondId));
            Assert.True(consultation.IsInProgress);
            Assert.Null(consultation.CompletedAt);
            Assert.Equal(1, (await context.Set<ActiveTargetsCache>().SingleAsync()).PlanVersion);
        }

        // "Lo que escribiste no se perdió": the retry publishes.
        var retried = ConsultationFlow.Ok(
            await ConsultationFlow.PublishAsync(db.Consultations, secondId, PractitionerId, "second"), "retry");
        Assert.Equal(2, retried.Plan.Version);
        await AssertNeverTwoActiveAsync(db);
    }

    /// <summary>
    ///     IA-6 / NC-4. Accepted Suggestion Is Traceable: the generation of an accepted AI suggestion is stored in
    ///     <c>nutritional_diagnoses.ai_generation_id</c> and survives the activation of step 4.
    /// </summary>
    [MySqlFact]
    public async Task An_accepted_ai_suggestion_keeps_its_generation_in_the_database()
    {
        const long generationId = 5_000_000_123L; // beyond int: the column is bigint
        await using var db = await MySqlIntegrationDatabase.CreateAsync(Today);
        await SeedBaselineAsync(db);
        var consultationId = await ConsultationFlow.StartAsync(db.Consultations, PatientId, PractitionerId);
        await ConsultationFlow.MeasureAsync(db.Consultations, consultationId, PractitionerId, 74.2m);
        var pending = ConsultationFlow.Ok(await db.Consultations.Handle(new IssueConsultationDiagnosisCommand(
            consultationId, PractitionerId, DiagnosisCode.OverweightGradeI, DiagnosisSource.AiSuggestionAccepted,
            generationId, "IMC 26.3 kg/m² con cintura de 88 cm.")), "diagnosis").Diagnosis;
        await ConsultationFlow.TargetsAsync(db.Consultations, consultationId, PractitionerId);
        ConsultationFlow.Ok(
            await ConsultationFlow.PublishAsync(db.Consultations, consultationId, PractitionerId, "publication"),
            "publication");

        await using var context = db.NewContext();
        var stored = await context.Set<NutritionalDiagnosis>().SingleAsync(d => d.Id == pending.Id);
        Assert.True(stored.IsActive);
        Assert.Equal(generationId, stored.AiGenerationId);
        Assert.Equal(DiagnosisSource.AiSuggestionAccepted, stored.Source!.Value);
        Assert.Equal(generationId, await context.Database
            .SqlQueryRaw<long>("SELECT ai_generation_id AS Value FROM nutritional_diagnoses WHERE id = {0}",
                pending.Id.Value)
            .SingleAsync());
    }

    /// <summary>(c) The unique index on the generated column rejects a second consultation in progress.</summary>
    [MySqlFact]
    public async Task The_database_rejects_two_consultations_in_progress_for_the_same_patient()
    {
        await using var db = await MySqlIntegrationDatabase.CreateAsync(Today);
        var first = new Consultation(new StartConsultationCommand(PatientId, PractitionerId), true);
        await using (var context = db.NewContext())
        {
            context.Add(first);
            await context.SaveChangesAsync();
        }

        await using (var context = db.NewContext())
        {
            // Bypasses the command service check on purpose: the index is the last line of defence.
            context.Add(new Consultation(new StartConsultationCommand(PatientId, PractitionerId), false));
            await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        }

        await using (var context = db.NewContext())
        {
            // Once the first one is no longer in progress, its generated column is NULL and a new one fits.
            var stored = await context.Set<Consultation>().SingleAsync();
            stored.Abandon();
            await context.SaveChangesAsync();
            context.Add(new Consultation(new StartConsultationCommand(PatientId, PractitionerId), false));
            await context.SaveChangesAsync();
            context.Add(new Consultation(new StartConsultationCommand(PatientId + 1, PractitionerId), true));
            await context.SaveChangesAsync();
            Assert.Equal(3, await context.Set<Consultation>().CountAsync());
        }
    }

    private static async Task SeedBaselineAsync(MySqlIntegrationDatabase db)
    {
        await using var scope = db.NewScope();
        await scope.ServiceProvider.GetRequiredService<IPatientBaselineRepository>().AddAsync(
            ConsultationScenario.Baseline(PatientId, PractitionerId, new DateOnly(1995, 3, 10), "Female", 168m,
                Today));
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().CompleteAsync();
    }

    /// <summary>"Never two active at once", read from the database.</summary>
    private static async Task AssertNeverTwoActiveAsync(MySqlIntegrationDatabase db)
    {
        await using var context = db.NewContext();
        var diagnoses = await context.Set<NutritionalDiagnosis>().Where(d => d.PatientId == PatientId).ToListAsync();
        var plans = await context.Set<NutritionPlan>().Where(p => p.PatientId == PatientId).ToListAsync();
        Assert.True(diagnoses.Count(d => d.IsActive) <= 1, "Two active diagnoses.");
        Assert.True(plans.Count(p => p.IsActive) <= 1, "Two active plan versions.");
    }
}
