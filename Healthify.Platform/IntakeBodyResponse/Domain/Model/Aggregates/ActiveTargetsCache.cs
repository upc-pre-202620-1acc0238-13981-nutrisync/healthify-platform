using Healthify.Platform.IntakeBodyResponse.Domain.Model.Commands;
using Healthify.Platform.IntakeBodyResponse.Domain.Model.ValueObjects;

namespace Healthify.Platform.IntakeBodyResponse.Domain.Model.Aggregates;

/// <summary>
///     The patient copy of the published contract: what to aim for today.
/// </summary>
/// <remarks>
///     Business rules: Published Contract Only, Diagnosis And Basis Never Cached and Cache Survives
///     Offline (Subflow 4.1).
///     The first two rules are structural rather than checked. There is no field on this class for a
///     diagnosis, a clinical rationale, an equation, a reference weight, an activity factor or a
///     deficit, so there is nowhere for them to be cached even by accident. What arrives is what the
///     published event carries, and the event carries the contract and nothing else.
///     The third rule is why this exists at all. The device must be able to show the targets with no
///     network, so they are stored here rather than fetched from Nutritional Care when needed.
///     The patient is the root. There is one cache per patient and it is replaced in place, because
///     an old version of the contract is of no use to anybody: the history of a plan lives in
///     Nutritional Care, which is where it belongs.
/// </remarks>
public partial class ActiveTargetsCache
{
    private List<string> _guidelines = [];
    private List<string> _restrictions = [];
    private List<CachedGuideline> _guidelineItems = [];
    private List<string> _legacyRestrictions = [];
    private List<CachedPlanChange>? _changesFromPrevious;

    /// <summary>Required by EF Core.</summary>
    protected ActiveTargetsCache()
    {
    }

    public ActiveTargetsCache(RefreshActiveTargetsCacheCommand command)
    {
        PatientId = command.PatientId;
        Apply(command);
    }

    /// <summary>The patient is the aggregate root: one cache each.</summary>
    public int PatientId { get; private set; }

    public int PlanVersion { get; private set; }
    public DateTimeOffset ValidFrom { get; private set; }

    public decimal EnergyKcal { get; private set; }
    public decimal ProteinG { get; private set; }
    public decimal CarbG { get; private set; }
    public decimal FatG { get; private set; }

    public DateTimeOffset RefreshedAt { get; private set; }

    /// <summary>From the practitioner: a catalog code or a custom text each. This context does not interpret them.</summary>
    public IReadOnlyList<string> Guidelines => _guidelines;

    /// <summary>Restriction codes since NC-6.</summary>
    public IReadOnlyList<string> Restrictions => _restrictions;

    /// <summary>NC-6. The same guidelines, telling catalog codes from custom texts.</summary>
    public IReadOnlyList<CachedGuideline> GuidelineItems => _guidelineItems;

    /// <summary>NC-6. Free text restrictions from before the closed list. The patient keeps seeing them.</summary>
    public IReadOnlyList<string> LegacyRestrictions => _legacyRestrictions;

    /// <summary>
    ///     NC-8. "Qué cambió en esta versión" (PT4, offline). Null when the contract was published before NC-8 and
    ///     carries none.
    /// </summary>
    public IReadOnlyList<CachedPlanChange>? ChangesFromPrevious => _changesFromPrevious;

    /// <summary>NC-9. The practitioner's message to the patient for this version, if any.</summary>
    public string? PatientMessage { get; private set; }

    /// <summary>Subflow 4.1 - a newer version of the contract arrived.</summary>
    public void Refresh(RefreshActiveTargetsCacheCommand command)
    {
        Apply(command);
    }

    private void Apply(RefreshActiveTargetsCacheCommand command)
    {
        if (command.PlanVersion <= 0)
            throw new ArgumentException("The published contract must carry a plan version.",
                nameof(command));
        if (command.EnergyKcal <= 0m)
            throw new ArgumentException("The published contract must carry an energy target.",
                nameof(command));

        PlanVersion = command.PlanVersion;
        ValidFrom = command.ValidFrom;

        EnergyKcal = decimal.Round(command.EnergyKcal, 2);
        ProteinG = decimal.Round(command.ProteinG, 2);
        CarbG = decimal.Round(command.CarbG, 2);
        FatG = decimal.Round(command.FatG, 2);

        _guidelines.Clear();
        _guidelines.AddRange(command.Guidelines
            .Where(g => !string.IsNullOrWhiteSpace(g)).Select(g => g.Trim()));
        _restrictions.Clear();
        _restrictions.AddRange(command.Restrictions
            .Where(r => !string.IsNullOrWhiteSpace(r)).Select(r => r.Trim()));

        // A contract published before NC-6 carries no items: its guidelines are custom texts, which is what
        // the data migration turned them into.
        _guidelineItems.Clear();
        _guidelineItems.AddRange(command.GuidelineItems
                                 ?? _guidelines.Select(g => new CachedGuideline(null, g)).ToList());
        _legacyRestrictions.Clear();
        _legacyRestrictions.AddRange((command.LegacyRestrictions ?? [])
            .Where(r => !string.IsNullOrWhiteSpace(r)).Select(r => r.Trim()));

        // NC-8: replaced with the contract, never merged: a contract without changes leaves none behind.
        _changesFromPrevious = command.ChangesFromPrevious?.ToList();
        PatientMessage = string.IsNullOrWhiteSpace(command.PatientMessage) ? null : command.PatientMessage.Trim();

        RefreshedAt = DateTimeOffset.UtcNow;
    }
}
