namespace Healthify.Platform.NutritionalCare.Domain.Model.Commands;

/// <summary>Optional biochemistry as it arrives with a command; validated by BiochemistryPanel.</summary>
public record BiochemistryDto(decimal? FastingGlucoseMgDl, decimal? TotalCholesterolMgDl, decimal? TriglyceridesMgDl);
