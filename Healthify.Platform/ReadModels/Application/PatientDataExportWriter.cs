using System.Globalization;
using System.IO.Compression;
using System.Text;
using Healthify.Platform.NutritionalCare.Interfaces.Acl;
using Healthify.Platform.ReadModels.Resources;
using Microsoft.Extensions.Localization;

namespace Healthify.Platform.ReadModels.Application;

/// <summary>
///     Data export. Writes a <see cref="PatientDataExportComposition" /> as a ZIP of CSV files, one per table (a single
///     CSV cannot hold tables of different shapes).
/// </summary>
/// <remarks>
///     Format: UTF-8 with BOM (so that Excel reads the accents), comma separator, CRLF line ends and fields escaped as
///     RFC 4180 says; ISO 8601 dates; numbers with a decimal point. Headers and the words of coded values are in the
///     patient's preferred language (IAM-3), from <see cref="DataExportMessages" />; an unknown code is written as is.
///     Text the patient or the catalog wrote is never translated.
/// </remarks>
public class PatientDataExportWriter(IStringLocalizer<DataExportMessages> localizer, TimeProvider timeProvider)
{
    public const string ProfileFile = "perfil.csv";
    public const string DiaryFile = "diario.csv";
    public const string WeighInsFile = "autopesajes.csv";
    public const string PlanVersionsFile = "metas.csv";
    public const string ConsultationsFile = "consultas.csv";
    public const string CareLinksFile = "vinculos.csv";

    private const string DefaultLanguage = "es";

    public PatientDataExportFile Write(PatientDataExportComposition composition)
    {
        var language = composition.Profile?.PreferredLanguage is "en" ? "en" : DefaultLanguage;

        // The localizer reads the current UI culture; this method is synchronous, so the switch cannot leak into
        // another request and is undone before returning.
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language);

            using var archive = new MemoryStream();
            using (var zip = new ZipArchive(archive, ZipArchiveMode.Create, true))
            {
                Add(zip, ProfileFile, Profile(composition));
                Add(zip, DiaryFile, Diary(composition));
                Add(zip, WeighInsFile, WeighIns(composition));
                Add(zip, PlanVersionsFile, PlanVersions(composition));
                Add(zip, ConsultationsFile, Consultations(composition));
                Add(zip, CareLinksFile, CareLinks(composition));
            }

            var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
            return new PatientDataExportFile(FileNameFor(today), archive.ToArray());
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }

    /// <summary>healthify-mis-datos-2026-10-21.zip</summary>
    public static string FileNameFor(DateOnly date)
    {
        return $"healthify-mis-datos-{date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}.zip";
    }

    private CsvTable Profile(PatientDataExportComposition c)
    {
        var table = new CsvTable(T("Profile_Name"), T("Profile_Email"), T("Profile_Language"),
            T("Profile_RegisteredAt"));
        if (c.Profile is { } p)
            table.Row(Csv.Text(p.FullName), Csv.Text(p.Email), Word(p.PreferredLanguage), Csv.Date(p.RegisteredAt));
        return table;
    }

    private CsvTable Diary(PatientDataExportComposition c)
    {
        var table = new CsvTable(T("Diary_LocalTimestamp"), T("Diary_Food"), T("Diary_Grams"), T("Diary_EnergyKcal"),
            T("Diary_ProteinG"), T("Diary_CarbG"), T("Diary_FatG"), T("Diary_Confirmed"), T("Diary_InPlan"),
            T("Diary_Origin"));
        foreach (var e in c.Diary)
            table.Row(Csv.Date(e.LocalTimestamp), Csv.Text(e.FoodName), Csv.Number(e.PortionGrams),
                Csv.Number(e.EnergyKcal), Csv.Number(e.ProteinG), Csv.Number(e.CarbG), Csv.Number(e.FatG),
                YesNo(e.IsConfirmed), Word(e.PlanAdherence), Word(e.Origin ?? e.Provenance, "Origin_"));
        return table;
    }

    private CsvTable WeighIns(PatientDataExportComposition c)
    {
        var table = new CsvTable(T("WeighIn_LocalTimestamp"), T("WeighIn_WeightKg"), T("WeighIn_Fasted"),
            T("WeighIn_FollowsProtocol"));
        foreach (var w in c.WeighIns)
            table.Row(Csv.Date(w.LocalTimestamp), Csv.Number(w.ValueKg), YesNo(w.Fasted), YesNo(w.FollowsProtocol));
        return table;
    }

    private CsvTable PlanVersions(PatientDataExportComposition c)
    {
        var table = new CsvTable(T("Plan_Version"), T("Plan_ValidFrom"), T("Plan_IsActive"), T("Plan_EnergyKcal"),
            T("Plan_ProteinG"), T("Plan_CarbG"), T("Plan_FatG"), T("Plan_ChangeReasonCode"), T("Plan_ChangeReason"));
        foreach (var v in c.PlanVersions)
            table.Row(Csv.Number(v.Version), Csv.Date(v.PublishedAt), YesNo(v.IsActive), Csv.Number(v.EnergyKcal),
                Csv.Number(v.ProteinG), Csv.Number(v.CarbG), Csv.Number(v.FatG), Csv.Text(v.ChangeReasonCode),
                Csv.Text(ChangeReason(v)));
        return table;
    }

    /// <summary>
    ///     One row per visit on the calendar, plus the completed consultations that no visit points to (a first
    ///     consultation held without one), oldest first.
    /// </summary>
    private CsvTable Consultations(PatientDataExportComposition c)
    {
        var table = new CsvTable(T("Consultation_ScheduledFor"), T("Consultation_Modality"), T("Consultation_State"),
            T("Consultation_Practitioner"), T("Consultation_CompletedAt"), T("Consultation_PlanVersion"));

        var byId = c.Consultations.ToDictionary(x => x.ConsultationId);
        var linked = c.FollowUps.Select(f => f.CompletedByConsultationId).OfType<int>().ToHashSet();

        var rows = c.FollowUps
            .Select(f =>
            {
                var consultation = f.CompletedByConsultationId is { } id && byId.TryGetValue(id, out var x) ? x : null;
                return (When: f.ScheduledFor, Cells: new[]
                {
                    Csv.Date(f.ScheduledFor), Word(f.Modality), Word(f.State), Csv.Text(NameOf(c, f.PractitionerId)),
                    Csv.Date(f.CompletedAt ?? consultation?.CompletedAt), Csv.Number(consultation?.PlanVersion)
                });
            })
            .Concat(c.Consultations
                .Where(x => !linked.Contains(x.ConsultationId))
                .Select(x => (When: x.CompletedAt, Cells: new[]
                {
                    Csv.Date(x.CompletedAt), "", Word("Completed"), "", Csv.Date(x.CompletedAt),
                    Csv.Number(x.PlanVersion)
                })))
            .OrderBy(r => r.When);

        foreach (var row in rows) table.Row(row.Cells);
        return table;
    }

    private CsvTable CareLinks(PatientDataExportComposition c)
    {
        var table = new CsvTable(T("Link_Practitioner"), T("Link_Status"), T("Link_EstablishedAt"),
            T("Link_ConsentGrantedAt"), T("Link_ConsentWithdrawnAt"), T("Link_AiProcessing"),
            T("Link_AiProcessingDecidedAt"), T("Link_EndedAt"), T("Link_EndReason"));
        foreach (var l in c.CareLinks)
            table.Row(Csv.Text(NameOf(c, l.PractitionerId)), Word(l.Status), Csv.Date(l.EstablishedAt),
                Csv.Date(l.ConsentGrantedAt), Csv.Date(l.ConsentWithdrawnAt), YesNo(l.AiProcessingGranted),
                Csv.Date(l.AiProcessingDecidedAt), Csv.Date(l.EndedAt),
                l.RevocationReason is null ? "" : Word(l.RevocationReason));
        return table;
    }

    /// <summary>
    ///     X-2. A system reason is worded here from its code and parameters, in the reader's language. A Custom reason
    ///     is the practitioner's clinical text and is not published by Nutritional Care: only a neutral sentence.
    /// </summary>
    private string? ChangeReason(PlanVersionHistoryItem version)
    {
        var date = version.ChangeReasonDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "";
        return version.ChangeReasonCode switch
        {
            null => null,
            "NewConsultation" => string.Format(CultureInfo.InvariantCulture, T("ChangeReason_NewConsultation"), date),
            "SignalAdjustment" => string.Format(CultureInfo.InvariantCulture, T("ChangeReason_SignalAdjustment"), date,
                version.ChangeReasonSignalType is null ? "" : Word(version.ChangeReasonSignalType)),
            _ => T("ChangeReason_Custom")
        };
    }

    private static string? NameOf(PatientDataExportComposition c, int practitionerId)
    {
        return c.PractitionerNames.TryGetValue(practitionerId, out var name) ? name : null;
    }

    private string T(string key)
    {
        return localizer[key].Value;
    }

    private string YesNo(bool value)
    {
        return T(value ? "Yes" : "No");
    }

    /// <summary>The word for a code (InPerson → Presencial), or the code itself when there is none.</summary>
    private string Word(string code, string prefix = "Value_")
    {
        var word = localizer[prefix + code];
        return Csv.Text(word.ResourceNotFound ? code : word.Value);
    }

    private static void Add(ZipArchive zip, string name, CsvTable table)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        using var stream = entry.Open();
        var bytes = Csv.Encoding.GetBytes(table.ToString());
        var preamble = Csv.Encoding.GetPreamble();
        stream.Write(preamble);
        stream.Write(bytes);
    }
}

/// <summary>A CSV table under construction: a header and rows of already formatted cells.</summary>
public sealed class CsvTable
{
    private readonly StringBuilder _builder = new();
    private readonly int _columns;

    public CsvTable(params string[] headers)
    {
        _columns = headers.Length;
        Append(headers.Select(Csv.Text).ToArray());
    }

    public void Row(params string[] cells)
    {
        if (cells.Length != _columns)
            throw new ArgumentException($"Expected {_columns} cells, got {cells.Length}.", nameof(cells));
        Append(cells);
    }

    public override string ToString()
    {
        return _builder.ToString();
    }

    private void Append(IEnumerable<string> cells)
    {
        _builder.Append(string.Join(Csv.Separator, cells.Select(Csv.Escape))).Append(Csv.LineEnd);
    }
}

/// <summary>RFC 4180 formatting of single cells.</summary>
public static class Csv
{
    public const char Separator = ',';
    public const string LineEnd = "\r\n";

    /// <summary>UTF-8 with BOM, so that Excel reads the accents.</summary>
    public static readonly UTF8Encoding Encoding = new(true);

    /// <summary>
    ///     RFC 4180: a field with a comma, a double quote, a CR or a LF is enclosed in double quotes, and a double quote
    ///     inside it is doubled.
    /// </summary>
    public static string Escape(string value)
    {
        return value.IndexOfAny([Separator, '"', '\r', '\n']) >= 0
            ? $"\"{value.Replace("\"", "\"\"")}\""
            : value;
    }

    /// <summary>
    ///     Free text. A text that starts like a spreadsheet formula (=, +, -, @, tab, CR) gets a leading apostrophe, so
    ///     that opening the copy never runs it (CSV injection).
    /// </summary>
    public static string Text(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        return value[0] is '=' or '+' or '-' or '@' or '\t' or '\r' ? "'" + value : value;
    }

    /// <summary>ISO 8601 with the offset: 2026-09-18T08:30:00-05:00.</summary>
    public static string Date(DateTimeOffset? value)
    {
        return value?.ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture) ?? "";
    }

    /// <summary>A decimal point, never a comma, and no trailing zeros: 74.2, 1850.</summary>
    public static string Number(decimal? value)
    {
        return value?.ToString("0.##", CultureInfo.InvariantCulture) ?? "";
    }

    public static string Number(int? value)
    {
        return value?.ToString(CultureInfo.InvariantCulture) ?? "";
    }
}
