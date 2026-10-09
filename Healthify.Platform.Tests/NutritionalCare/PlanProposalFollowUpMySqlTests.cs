using Healthify.Platform.NutritionalCare.Domain.Model.Aggregates;
using Healthify.Platform.NutritionalCare.Domain.Model.Commands;
using Healthify.Platform.NutritionalCare.Domain.Model.ValueObjects;
using Healthify.Platform.NutritionalCare.Domain.Repositories;
using Healthify.Platform.Shared.Domain.Repositories;
using Healthify.Platform.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Healthify.Platform.Tests.NutritionalCare;

/// <summary>
///     Against a real MySQL server: the message of the EV-5 draft lives in the JSON of
///     <c>consultations.publication_draft</c> (drafts saved before it still read, without a message), and the recovery
///     of the proposal queue finds open sustained deviations without a proposal by their real <c>created_at</c>.
/// </summary>
/// <remarks>Skipped unless <c>HEALTHIFY_IT_MYSQL</c> is set (CLAUDE.md §5).</remarks>
[Collection(MySqlCollection.Name)]
[Trait("Category", "MySql")]
public class PlanProposalFollowUpMySqlTests
{
    private const int PatientId = 10;
    private const int PractitionerId = 20;
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    [MySqlFact]
    public async Task The_draft_message_round_trips_and_old_drafts_read_without_one()
    {
        await using var db = await MySqlIntegrationDatabase.CreateAsync(Today);
        await SeedBaselineAsync(db);
        var consultationId = await ConsultationFlow.StartAsync(db.Consultations, PatientId, PractitionerId);
        await ConsultationFlow.MeasureAsync(db.Consultations, consultationId, PractitionerId, 80m);
        await ConsultationFlow.DiagnoseAsync(db.Consultations, consultationId, PractitionerId,
            DiagnosisCode.ObesityGradeI);
        await ConsultationFlow.TargetsAsync(db.Consultations, consultationId, PractitionerId);

        ConsultationFlow.Ok(await db.Consultations.Handle(new SaveConsultationPublicationDraftCommand(consultationId,
            PractitionerId, [DietaryRestriction.LactoseFree], [Guideline.ReduceSalt], ["Caminar 20 minutos"],
            "Probemos con cenas completas.")), "draft");

        await using (var context = db.NewContext())
        {
            var draft = (await context.Set<Consultation>().SingleAsync(c => c.Id == new ConsultationId(consultationId)))
                .PublicationDraft!;
            Assert.Equal("Probemos con cenas completas.", draft.PatientMessage);
            Assert.Equal([Guideline.ReduceSalt], draft.Guidelines);
            Assert.Equal(["Caminar 20 minutos"], draft.CustomGuidelines);

            // A draft saved before NC-9 has no message in its JSON.
            await context.Database.ExecuteSqlRawAsync(
                "UPDATE consultations SET publication_draft = {0} WHERE id = {1}",
                "{\"Restrictions\":[],\"Guidelines\":[\"ReduceSalt\"],\"CustomGuidelines\":[]}", consultationId);
        }

        await using (var context = db.NewContext())
        {
            var draft = (await context.Set<Consultation>().SingleAsync(c => c.Id == new ConsultationId(consultationId)))
                .PublicationDraft!;
            Assert.Null(draft.PatientMessage);
            Assert.Equal([Guideline.ReduceSalt], draft.Guidelines);
        }
    }

    [MySqlFact]
    public async Task Recovery_finds_open_sustained_deviations_without_a_proposal_by_creation_time()
    {
        await using var db = await MySqlIntegrationDatabase.CreateAsync(Today);
        int waitingId, proposedId;
        await using (var scope = db.NewScope())
        {
            var repository = scope.ServiceProvider.GetRequiredService<IReviewItemRepository>();
            var waiting = Item(PatientId);
            var proposed = Item(PatientId + 1);
            proposed.AttachProposal(1, "t", 1650m, 95m, 190m, 60m, [], [], "Probemos con estas ideas.", 7, "r",
                DateTimeOffset.UtcNow);
            var escalation = new ReviewItem(new OpenReviewItemCommand(PatientId + 2, SignalType.ConsistencyEscalation,
                "Escalated"), PractitionerId);
            await repository.AddAsync(waiting);
            await repository.AddAsync(proposed);
            await repository.AddAsync(escalation);
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().CompleteAsync();
            waitingId = waiting.Id.Value;
            proposedId = proposed.Id.Value;
        }

        await using (var scope = db.NewScope())
        {
            var repository = scope.ServiceProvider.GetRequiredService<IReviewItemRepository>();
            var found = await repository.ListOpenWithoutProposalAsync(new SignalType(SignalType.SustainedDeviation),
                DateTimeOffset.UtcNow.AddHours(-72), 50);
            Assert.Equal([waitingId], found.Select(r => r.Id.Value));
            Assert.DoesNotContain(found, r => r.Id.Value == proposedId);

            // Created before the window: not recovered.
            Assert.Empty(await repository.ListOpenWithoutProposalAsync(new SignalType(SignalType.SustainedDeviation),
                DateTimeOffset.UtcNow.AddHours(1), 50));
        }
    }

    private static ReviewItem Item(int patientId)
    {
        return new ReviewItem(new OpenReviewItemCommand(patientId, SignalType.SustainedDeviation,
            "Mean energy 40.0% below", new ReviewItemEvidenceDto(-40m, 5, 9, "Below")), PractitionerId);
    }

    private static async Task SeedBaselineAsync(MySqlIntegrationDatabase db)
    {
        await using var scope = db.NewScope();
        await scope.ServiceProvider.GetRequiredService<IPatientBaselineRepository>().AddAsync(
            ConsultationScenario.Baseline(PatientId, PractitionerId, new DateOnly(1995, 3, 10), "Female", 168m,
                Today));
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().CompleteAsync();
    }
}
