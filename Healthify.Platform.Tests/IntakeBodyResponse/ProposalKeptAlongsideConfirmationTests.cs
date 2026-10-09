using Healthify.Platform.IntakeBodyResponse.Domain.Model.Aggregates;
using Healthify.Platform.IntakeBodyResponse.Domain.Model.ValueObjects;

namespace Healthify.Platform.Tests.IntakeBodyResponse;

/// <summary>
///     Characterization of the rule Proposal Kept Alongside Confirmation (Subflow 4.2): what the
///     device proposed and what the patient confirmed live side by side and never overwrite each other.
/// </summary>
public class ProposalKeptAlongsideConfirmationTests
{
    private const int ProposedFoodId = 12;
    private const decimal ProposedGrams = 320m;

    // IN-1 made the answer to «¿Esta comida estaba en tu plan?» part of every confirmation. The rule
    // characterized here does not depend on which answer is given.
    private static readonly PlanAdherence Answer = new(PlanAdherence.InPlan);

    [Fact]
    public void Confirming_copies_the_proposal_and_leaves_it_untouched()
    {
        var entry = PhotoEntryWithProposal();

        entry.ConfirmProposedEstimate(Answer);

        Assert.True(entry.HasConfirmedEstimate);
        Assert.Equal(ProposedFoodId, entry.ProposedReferenceFoodId);
        Assert.Equal(ProposedGrams, entry.ProposedPortionGrams);
        Assert.Equal(0.8m, entry.ProposedConfidence);
        Assert.Equal(ProposedFoodId, entry.ConfirmedReferenceFoodId);
        Assert.Equal(ProposedGrams, entry.ConfirmedPortionGrams);
    }

    [Fact]
    public void Adjusting_writes_the_correction_beside_the_proposal()
    {
        var entry = PhotoEntryWithProposal();

        entry.AdjustProposedEstimate(referenceFoodId: 15, portionGrams: 300m, Answer);

        Assert.Equal(ProposedFoodId, entry.ProposedReferenceFoodId);
        Assert.Equal(ProposedGrams, entry.ProposedPortionGrams);
        Assert.Equal(15, entry.ConfirmedReferenceFoodId);
        Assert.Equal(300m, entry.ConfirmedPortionGrams);
    }

    [Fact]
    public void A_confirmation_happens_once_and_a_proposal_never_arrives_after_it()
    {
        var entry = PhotoEntryWithProposal();
        entry.ConfirmProposedEstimate(Answer);

        Assert.Throws<InvalidOperationException>(() => entry.ConfirmProposedEstimate(Answer));
        Assert.Throws<InvalidOperationException>(() => entry.AdjustProposedEstimate(15, 300m, Answer));
        Assert.Throws<InvalidOperationException>(() => entry.ProposeEstimate(
            new ProposedEstimate(99, 100m, new Confidence(0.5m), DateTimeOffset.UtcNow)));
        Assert.Equal(ProposedGrams, entry.ProposedPortionGrams);
    }

    [Fact]
    public void Nothing_can_be_confirmed_without_a_proposal()
    {
        var entry = NewEntry(Provenance.Photo);

        Assert.Throws<InvalidOperationException>(() => entry.ConfirmProposedEstimate(Answer));
        Assert.False(entry.HasConfirmedEstimate);
    }

    private static DiaryEntry PhotoEntryWithProposal()
    {
        var entry = NewEntry(Provenance.Photo);
        entry.ProposeEstimate(new ProposedEstimate(ProposedFoodId, ProposedGrams, new Confidence(0.8m),
            DateTimeOffset.UtcNow));
        return entry;
    }

    private static DiaryEntry NewEntry(string provenance)
    {
        return new DiaryEntry(1, new LocalTimestamp(DateTimeOffset.UtcNow.AddHours(-1)),
            new Provenance(provenance), new SyncState(SyncState.Synced));
    }
}
