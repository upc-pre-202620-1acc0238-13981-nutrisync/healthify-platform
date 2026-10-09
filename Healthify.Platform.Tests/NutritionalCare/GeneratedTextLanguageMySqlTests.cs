using Healthify.Platform.NutritionalCare.Application.CommandServices;
using Healthify.Platform.NutritionalCare.Domain.Model.Aggregates;
using Healthify.Platform.NutritionalCare.Domain.Model.Commands;
using Healthify.Platform.NutritionalCare.Domain.Model.Entities;
using Healthify.Platform.NutritionalCare.Domain.Model.Errors;
using Healthify.Platform.NutritionalCare.Domain.Model.ValueObjects;
using Healthify.Platform.NutritionalCare.Domain.Repositories;
using Healthify.Platform.Shared.Application.Patterns;
using Healthify.Platform.Shared.Domain.Repositories;
using Healthify.Platform.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Healthify.Platform.Tests.NutritionalCare;

/// <summary>
///     X-2 against a real MySQL server: the change reason code and its data, and the two languages of a plan proposal,
///     persist and read back; and the migration <c>NutritionalCare_ChangeReasonCodesAndProposalLanguages</c>, run down
///     and up again over existing rows, recognises the two sentences the system wrote before X-2 and leaves everything
///     else as Custom.
/// </summary>
/// <remarks>Skipped unless <c>HEALTHIFY_IT_MYSQL</c> is set (CLAUDE.md §5).</remarks>
[Collection(MySqlCollection.Name)]
[Trait("Category", "MySql")]
public class GeneratedTextLanguageMySqlTests
{
    private const int PatientId = 10;
    private const int PractitionerId = 20;
    private const string PreviousMigration = "20261006185307_NutritionalCare_AddReviewItemPlanProposals";
    private static readonly DateOnly Today = new(2026, 9, 18);

    [MySqlFact]
    public async Task Codes_and_languages_persist_and_the_backfill_recognises_the_old_sentences()
    {
        await using var db = await MySqlIntegrationDatabase.CreateAsync(Today);
        await using (var scope = db.NewScope())
        {
            await scope.ServiceProvider.GetRequiredService<IPatientBaselineRepository>().AddAsync(
                ConsultationScenario.Baseline(PatientId, PractitionerId, new DateOnly(1995, 3, 10), "Female", 168m,
                    Today));
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().CompleteAsync();
        }

        // v1 and v2 from consultations, v3 to v6 adjusted by the practitioner.
        await ConsultationFlow.CompleteAsync(db.Consultations, PatientId, PractitionerId, 80m,
            DiagnosisCode.ObesityGradeI, "first-publication");
        var v2 = (await ConsultationFlow.CompleteAsync(db.Consultations, PatientId, PractitionerId, 78m,
            DiagnosisCode.ObesityGradeI, "second-publication")).Plan;
        var current = v2;
        foreach (var reason in new[] { "Ajuste 3", "Ajuste 4", "Ajuste 5", "Ajuste 6" })
            current = await AdjustAsync(db, current, reason);
        var proposalItemId = await SeedItemWithProposalAsync(db, current);

        // Written by the code of X-2.
        await using (var context = db.NewContext())
        {
            var plans = await PlansAsync(context);
            Assert.Null(plans[1].ChangeReason);
            Assert.Equal((ChangeReason.NewConsultationCode, new ChangeReasonData(Today, null)),
                (plans[2].ChangeReason!.Code, plans[2].ChangeReason!.Data));
            Assert.Equal("Nueva consulta del 18 sept. 2026", plans[2].ChangeReason!.Value);
            Assert.Equal((ChangeReason.Custom, (ChangeReasonData?)null, "Ajuste 3"),
                (plans[3].ChangeReason!.Code, plans[3].ChangeReason!.Data, plans[3].ChangeReason!.Value));

            var proposal = await context.Set<PlanAdjustmentProposal>().SingleAsync();
            Assert.Equal(("en", "es"), (proposal.PractitionerLanguage, proposal.PatientLanguage));
        }

        // Rows as the code before X-2 left them: the sentences only.
        await using (var context = db.NewContext())
        {
            await context.Database.ExecuteSqlRawAsync(
                "UPDATE nutrition_plans SET change_reason = CASE version " +
                "WHEN 3 THEN 'Ajuste por señal: desviación sostenida del 8 sept. 2026' " +
                "WHEN 4 THEN 'Nueva consulta del 1 dic. 2025' " +
                "WHEN 5 THEN 'Nueva consulta del paciente, retomó el control' " +
                "ELSE change_reason END WHERE patient_id = {0}", PatientId);
            var migrator = context.GetService<IMigrator>();
            await migrator.MigrateAsync(PreviousMigration);
            Assert.Equal(0L, await context.Database.SqlQueryRaw<long>(
                    "SELECT COUNT(*) AS Value FROM information_schema.columns WHERE table_schema = DATABASE() " +
                    "AND column_name IN ('change_reason_code', 'change_reason_data', 'practitioner_language', " +
                    "'patient_language')")
                .SingleAsync());
            await migrator.MigrateAsync();
        }

        await using (var context = db.NewContext())
        {
            var plans = await PlansAsync(context);
            Assert.Null(plans[1].ChangeReason);
            Assert.Equal((ChangeReason.NewConsultationCode, new ChangeReasonData(Today, null)),
                (plans[2].ChangeReason!.Code, plans[2].ChangeReason!.Data));
            Assert.Equal((ChangeReason.SignalAdjustmentCode,
                    new ChangeReasonData(new DateOnly(2026, 9, 8), SignalType.SustainedDeviation)),
                (plans[3].ChangeReason!.Code, plans[3].ChangeReason!.Data));
            Assert.Equal("Ajuste por señal: desviación sostenida del 8 sept. 2026", plans[3].ChangeReason!.Value);
            Assert.Equal((ChangeReason.NewConsultationCode, new ChangeReasonData(new DateOnly(2025, 12, 1), null)),
                (plans[4].ChangeReason!.Code, plans[4].ChangeReason!.Data));
            Assert.Equal((ChangeReason.Custom, "Nueva consulta del paciente, retomó el control"),
                (plans[5].ChangeReason!.Code, plans[5].ChangeReason!.Value));
            Assert.Equal((ChangeReason.Custom, "Ajuste 6"), (plans[6].ChangeReason!.Code, plans[6].ChangeReason!.Value));
            // The first version has no reason and gets no code.
            Assert.Equal(1L, await context.Database.SqlQueryRaw<long>(
                "SELECT COUNT(*) AS Value FROM nutrition_plans WHERE change_reason IS NULL " +
                "AND change_reason_code IS NULL AND change_reason_data IS NULL").SingleAsync());

            // A proposal generated before X-2 has no languages.
            var proposal = await context.Set<PlanAdjustmentProposal>().SingleAsync();
            Assert.Equal(new ReviewItemId(proposalItemId), proposal.ReviewItemId);
            Assert.Null(proposal.PractitionerLanguage);
            Assert.Null(proposal.PatientLanguage);
        }
    }

    [MySqlFact]
    public async Task Resolution_note_codes_persist_and_the_backfill_recognises_the_old_notes()
    {
        await using var db = await MySqlIntegrationDatabase.CreateAsync(Today);
        var asIs = WithProposal();
        asIs.ResolveByAssigningPlan(2, acceptedAsIs: true);
        var withEdits = WithProposal();
        withEdits.ResolveByAssigningPlan(13, acceptedAsIs: false);
        var custom = WithProposal();
        custom.Resolve(false, "Lo conversamos en la consulta");
        var discarded = WithProposal();
        discarded.Resolve(false, null);
        var plain = new ReviewItem(new OpenReviewItemCommand(PatientId, SignalType.ConsistencyEscalation, "e"),
            PractitionerId);
        plain.Resolve(true, null);
        var open = new ReviewItem(new OpenReviewItemCommand(PatientId, SignalType.ScheduledRecheck, "e"),
            PractitionerId);
        ReviewItem[] items = [asIs, withEdits, custom, discarded, plain, open];
        await using (var scope = db.NewScope())
        {
            var repository = scope.ServiceProvider.GetRequiredService<IReviewItemRepository>();
            foreach (var item in items) await repository.AddAsync(item);
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().CompleteAsync();
        }

        var expected = new (string? Code, ResolutionNoteData? Data, string? Note)[]
        {
            (ResolutionNoteCode.PlanAssignedAsIs, new ResolutionNoteData(2),
                "Plan v2 asignado (propuesta IA aceptada tal cual)"),
            (ResolutionNoteCode.PlanAssignedWithEdits, new ResolutionNoteData(13),
                "Plan v13 asignado (propuesta IA aceptada con ediciones)"),
            (ResolutionNoteCode.Custom, null, "Lo conversamos en la consulta"),
            (ResolutionNoteCode.ProposalDiscarded, null, null),
            (null, null, null),
            (null, null, null)
        };

        // Written by the code of X-2, then rebuilt by the backfill from the notes alone (down and up again).
        await AssertNotesAsync(db, items, expected);
        await using (var context = db.NewContext())
        {
            var migrator = context.GetService<IMigrator>();
            await migrator.MigrateAsync("20261007030850_NutritionalCare_ChangeReasonCodesAndProposalLanguages");
            Assert.Equal(0L, await context.Database.SqlQueryRaw<long>(
                    "SELECT COUNT(*) AS Value FROM information_schema.columns WHERE table_schema = DATABASE() " +
                    "AND column_name IN ('resolution_note_code', 'resolution_note_data')")
                .SingleAsync());
            await migrator.MigrateAsync();
        }

        await AssertNotesAsync(db, items, expected);
    }

    private static ReviewItem WithProposal()
    {
        var item = new ReviewItem(new OpenReviewItemCommand(PatientId, SignalType.SustainedDeviation,
            "Mean energy 40.0% below", new ReviewItemEvidenceDto(-40m, 5, 9, "Below")), PractitionerId);
        item.AttachProposal(1, "Ajuste", 1650m, 95m, 190m, 60m, [], [], "Probemos con estas ideas.", 7, "Por qué",
            DateTimeOffset.UtcNow, "es", "es");
        return item;
    }

    private static async Task AssertNotesAsync(MySqlIntegrationDatabase db, ReviewItem[] items,
        (string? Code, ResolutionNoteData? Data, string? Note)[] expected)
    {
        await using var context = db.NewContext();
        var stored = await context.Set<ReviewItem>().ToDictionaryAsync(r => r.Id.Value);
        for (var i = 0; i < items.Length; i++)
        {
            var row = stored[items[i].Id.Value];
            Assert.Equal(expected[i], (row.ResolutionNoteCode, row.ResolutionNoteData, row.ResolutionNote));
        }
    }

    private static async Task<Dictionary<int, NutritionPlan>> PlansAsync(DbContext context)
    {
        return await context.Set<NutritionPlan>().Where(p => p.PatientId == PatientId)
            .ToDictionaryAsync(p => p.Version);
    }

    private static async Task<NutritionPlan> AdjustAsync(MySqlIntegrationDatabase db, NutritionPlan plan,
        string reason)
    {
        await using var scope = db.NewScope();
        var targets = plan.PrescribedTargets!;
        var result = await scope.ServiceProvider.GetRequiredService<INutritionPlanCommandService>().Handle(
            new AdjustNutritionPlanCommand(plan.Id.Value, PractitionerId, targets.EnergyKcal - 40m, targets.ProteinG,
                targets.CarbG - 10m, targets.FatG, [Guideline.ReduceSalt], plan.Restrictions.ToList(), reason));
        return Assert.IsType<Result<NutritionPlan, NutritionalCareError>.Success>(result).Value;
    }

    private static async Task<int> SeedItemWithProposalAsync(MySqlIntegrationDatabase db, NutritionPlan plan)
    {
        await using var scope = db.NewScope();
        var targets = plan.PrescribedTargets!;
        var energy = decimal.Round(targets.EnergyKcal * 0.92m, 0);
        var protein = decimal.Round(targets.ProteinG, 0);
        var fat = decimal.Round(targets.FatG * 0.92m, 0);
        var carb = decimal.Round((energy - 4m * protein - 9m * fat) / 4m, 0);

        var item = new ReviewItem(new OpenReviewItemCommand(PatientId, SignalType.SustainedDeviation,
            "Mean energy 40.0% below", new ReviewItemEvidenceDto(-40m, 5, 9, "Below")), PractitionerId);
        item.AttachProposal(1, "Adjust the energy and strengthen dinners", energy, protein, carb, fat, [], [],
            "Notamos que tus cenas son más ligeras.", 7, "Logged 40 % below the target on 5 of 9 logged days.",
            DateTimeOffset.UtcNow, "en", "es");
        await scope.ServiceProvider.GetRequiredService<IReviewItemRepository>().AddAsync(item);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().CompleteAsync();
        return item.Id.Value;
    }
}
