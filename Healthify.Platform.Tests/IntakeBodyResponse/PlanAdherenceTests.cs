using Healthify.Platform.IntakeBodyResponse.Domain.Model.Aggregates;
using Healthify.Platform.IntakeBodyResponse.Domain.Model.ValueObjects;

namespace Healthify.Platform.Tests.IntakeBodyResponse;

/// <summary>IN-1. «¿Esta comida estaba en tu plan?» as an attribute of an ordinary entry.</summary>
public class PlanAdherenceTests
{
    private static readonly PlanAdherence InPlan = new(PlanAdherence.InPlan);
    private static readonly PlanAdherence OffPlan = new(PlanAdherence.OffPlan);
    private static readonly PlanAdherence NotAnswered = new(PlanAdherence.NotAnswered);

    [Theory]
    [InlineData("InPlan", true)]
    [InlineData("offplan", true)]
    [InlineData("NotAnswered", false)]
    public void Accepts_the_three_values_case_insensitively(string value, bool answered)
    {
        Assert.Equal(answered, new PlanAdherence(value).IsAnswered);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Yes")]
    public void Rejects_anything_else(string value)
    {
        Assert.Throws<ArgumentException>(() => new PlanAdherence(value));
    }

    [Fact]
    public void A_new_entry_starts_not_answered()
    {
        Assert.Equal(PlanAdherence.NotAnswered, NewEntry(Provenance.Photo).PlanAdherence.Value);
    }

    [Fact]
    public void A_new_entry_cannot_use_off_plan_as_provenance()
    {
        Assert.Throws<ArgumentException>(() => NewEntry(Provenance.OffPlan));
    }

    [Fact]
    public void The_legacy_factory_still_creates_the_one_tap_off_plan_entry()
    {
        var entry = DiaryEntry.LegacyOffPlan(1, new LocalTimestamp(DateTimeOffset.UtcNow),
            new SyncState(SyncState.Synced));

        Assert.True(entry.Provenance.IsOffPlan);
        Assert.True(entry.PlanAdherence.IsOffPlan);
        Assert.False(entry.HasConfirmedEstimate);
    }

    [Fact]
    public void Every_interactive_confirmation_requires_an_answer()
    {
        var photo = PhotoWithProposal();
        Assert.Throws<ArgumentException>(() => photo.ConfirmProposedEstimate(NotAnswered));
        Assert.Throws<ArgumentException>(() => photo.AdjustProposedEstimate(15, 300m, NotAnswered));
        Assert.False(photo.HasConfirmedEstimate);

        var manual = NewEntry(Provenance.Manual);
        Assert.Throws<ArgumentException>(() => manual.ConfirmDirectly(Confirmed(), NotAnswered));
        Assert.False(manual.HasConfirmedEstimate);
    }

    [Fact]
    public void Off_plan_is_recorded_with_the_confirmation_and_the_entry_counts()
    {
        var entry = NewEntry(Provenance.Manual);

        entry.ConfirmDirectly(Confirmed(), OffPlan);

        Assert.True(entry.PlanAdherence.IsOffPlan);
        Assert.True(entry.HasConfirmedEstimate);
        Assert.True(entry.Provenance.IsManual);
    }

    [Fact]
    public void Adjusting_records_the_answer_given_with_the_correction()
    {
        var entry = PhotoWithProposal();

        entry.AdjustProposedEstimate(15, 300m, OffPlan);

        Assert.True(entry.PlanAdherence.IsOffPlan);
    }

    [Fact]
    public void A_legacy_sync_confirmation_stays_not_answered()
    {
        var entry = NewEntry(Provenance.Manual);

        entry.ConfirmFromLegacySync(Confirmed());

        Assert.True(entry.HasConfirmedEstimate);
        Assert.False(entry.PlanAdherence.IsAnswered);
    }

    [Fact]
    public void A_later_copy_fills_a_missing_answer_once_and_never_rewrites_it()
    {
        var entry = NewEntry(Provenance.Manual);
        entry.ConfirmFromLegacySync(Confirmed());

        Assert.True(entry.ResolveWithLatest(12, 280m, DateTimeOffset.UtcNow, OffPlan));
        Assert.True(entry.PlanAdherence.IsOffPlan);

        Assert.False(entry.ResolveWithLatest(12, 280m, DateTimeOffset.UtcNow, InPlan));
        Assert.True(entry.PlanAdherence.IsOffPlan);
    }

    private static ConfirmedEstimate Confirmed()
    {
        return new ConfirmedEstimate(12, 280m, DateTimeOffset.UtcNow);
    }

    private static DiaryEntry PhotoWithProposal()
    {
        var entry = NewEntry(Provenance.Photo);
        entry.ProposeEstimate(new ProposedEstimate(12, 320m, new Confidence(0.8m), DateTimeOffset.UtcNow));
        return entry;
    }

    private static DiaryEntry NewEntry(string provenance)
    {
        return new DiaryEntry(1, new LocalTimestamp(DateTimeOffset.UtcNow.AddHours(-1)),
            new Provenance(provenance), new SyncState(SyncState.Synced));
    }
}
