using Healthify.Platform.FoodCatalog.Application.Internal;
using Healthify.Platform.FoodCatalog.Domain.Model.Aggregates;
using Healthify.Platform.FoodCatalog.Domain.Model.Commands;
using Healthify.Platform.FoodCatalog.Interfaces.REST.Resources;

namespace Healthify.Platform.FoodCatalog.Interfaces.REST.Transform;

// ---------------------------------------------------------------------------------------------
// Resource to Command
// ---------------------------------------------------------------------------------------------

public static class CreateLocalOverrideCommandAssembler
{
    public static CreateLocalOverrideCommand ToCommand(int practitionerId,
        CreateLocalOverrideResource resource)
    {
        return new CreateLocalOverrideCommand(practitionerId, resource.LocalName,
            resource.EnergyKcalPer100g, resource.ProteinGPer100g, resource.CarbGPer100g,
            resource.FatGPer100g);
    }
}

public static class ImportCatalogSnapshotCommandAssembler
{
    public static ImportCatalogSnapshotCommand ToCommand(ImportCatalogSnapshotResource resource)
    {
        return new ImportCatalogSnapshotCommand(resource.Term, resource.Max);
    }
}

// ---------------------------------------------------------------------------------------------
// Aggregate to Resource
// ---------------------------------------------------------------------------------------------

public static class ReferenceFoodResourceAssembler
{
    /// <summary>The source hash is deliberately absent: it is stored, never published.</summary>
    public static ReferenceFoodResource ToResource(ReferenceFood referenceFood)
    {
        return new ReferenceFoodResource(
            referenceFood.Id.Value,
            referenceFood.LocalNameText,
            referenceFood.EnergyKcalPer100g,
            referenceFood.ProteinGPer100g,
            referenceFood.CarbGPer100g,
            referenceFood.FatGPer100g,
            referenceFood.IsLocalOverride);
    }
}

public static class CatalogImportSummaryResourceAssembler
{
    public static CatalogImportSummaryResource ToResource(CatalogImportSummary summary)
    {
        return new CatalogImportSummaryResource(summary.Term, summary.ProvidersConsulted,
            summary.TranslatedCount, summary.FailedCount);
    }
}
