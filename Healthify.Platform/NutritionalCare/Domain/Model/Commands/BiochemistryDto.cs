namespace Healthify.Platform.NutritionalCare.Domain.Model.Commands;

/// <summary>NC-3. Optional biochemistry as it arrives with a command; validated by BiochemistryPanel.</summary>
public record BiochemistryDto(decimal? FastingGlucoseMgDl, decimal? TotalCholesterolMgDl, decimal? TriglyceridesMgDl);
