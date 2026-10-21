using System.IO.Compression;
using System.Reflection;
using System.Security.Claims;
using System.Text;
using Healthify.Platform.CareRelationship.Interfaces.Acl;
using Healthify.Platform.Iam.Interfaces.Acl;
using Healthify.Platform.IntakeBodyResponse.Application.Acl;
using Healthify.Platform.IntakeBodyResponse.Application.QueryServices;
using Healthify.Platform.IntakeBodyResponse.Domain.Model.Aggregates;
using Healthify.Platform.IntakeBodyResponse.Domain.Model.Queries;
using Healthify.Platform.IntakeBodyResponse.Domain.Model.ValueObjects;
using Healthify.Platform.IntakeBodyResponse.Domain.Services;
using Healthify.Platform.IntakeBodyResponse.Interfaces.Acl;
using Healthify.Platform.FoodCatalog.Interfaces.Acl;
using Healthify.Platform.MonitoringAdherence.Interfaces.Acl;
using Healthify.Platform.NutritionalCare.Interfaces.Acl;
using Healthify.Platform.ReadModels.Application;
using Healthify.Platform.ReadModels.Interfaces.REST;
using Healthify.Platform.ReadModels.Resources;
using Healthify.Platform.Shared.Interfaces.REST.ProblemDetails;
using Healthify.Platform.Shared.Resources;
using Healthify.Platform.Tests.TestSupport;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Healthify.Platform.Tests.ReadModels;

/// <summary>
///     Data export (GET /patients/{id}/data-export). The patient downloads a ZIP of CSV files with their own data, in
///     their language, and never a diagnosis, rationale, calculation basis or BMI.
/// </summary>
public class PatientDataExportTests
{
    private const int PatientId = 10;
    private const int PractitionerId = 20;
    private static readonly DateTimeOffset Now = new(2026, 10, 21, 15, 0, 0, TimeSpan.Zero);

    private static readonly string[] ExpectedFiles =
    [
        "perfil.csv", "diario.csv", "autopesajes.csv", "metas.csv", "consultas.csv", "vinculos.csv"
    ];

    private readonly IIamContextFacade _iam = Substitute.For<IIamContextFacade>();
    private readonly IIntakeContextFacade _intake = Substitute.For<IIntakeContextFacade>();
    private readonly INutritionalCareContextFacade _nutritionalCare = Substitute.For<INutritionalCareContextFacade>();
    private readonly IMonitoringContextFacade _monitoring = Substitute.For<IMonitoringContextFacade>();
    private readonly ICareRelationshipContextFacade _careRelationship = Substitute.For<ICareRelationshipContextFacade>();

    public PatientDataExportTests()
    {
        GivenLanguage("es");
        _iam.GetUsersByIds(Arg.Any<IEnumerable<int>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<int, UserIdentityItem>
            {
                [PractitionerId] = new(PractitionerId, "rosa@correo.com", "Practitioner", "Rosa", "Medina")
            });
        _intake.GetDiaryExport(PatientId, Arg.Any<CancellationToken>()).Returns([
            new DiaryExportEntryItem(new DateTimeOffset(2026, 9, 18, 13, 5, 0, TimeSpan.FromHours(-5)),
                "Arroz, \"con\" pollo\ny ensalada", 250m, 412.5m, 20.1m, 55m, 9.75m, true, "OffPlan", "Manual",
                "MealIdea")
        ]);
        _intake.GetSelfWeighIns(PatientId, Arg.Any<CancellationToken>()).Returns([
            new SelfWeighInExportItem(new DateTimeOffset(2026, 9, 19, 7, 30, 0, TimeSpan.FromHours(-5)), 74.2m, true,
                true)
        ]);
        _nutritionalCare.GetPlanVersionHistory(PatientId, Arg.Any<CancellationToken>()).Returns([
            new PlanVersionHistoryItem(1, new DateTimeOffset(2026, 3, 12, 15, 0, 0, TimeSpan.Zero), false, 1800m, 90m,
                200m, 60m, null, null, null),
            new PlanVersionHistoryItem(2, new DateTimeOffset(2026, 9, 18, 15, 0, 0, TimeSpan.Zero), true, 1650.5m,
                95m, 180m, 55m, "NewConsultation", new DateOnly(2026, 9, 18), null),
            new PlanVersionHistoryItem(3, new DateTimeOffset(2026, 10, 1, 15, 0, 0, TimeSpan.Zero), false, 1600m, 95m,
                170m, 55m, "Custom", null, null)
        ]);
        _nutritionalCare.GetCompletedConsultations(PatientId, Arg.Any<CancellationToken>()).Returns([
            new CompletedConsultationItem(2, new DateTimeOffset(2026, 9, 18, 15, 30, 0, TimeSpan.Zero), 2, false),
            new CompletedConsultationItem(1, new DateTimeOffset(2026, 3, 12, 15, 0, 0, TimeSpan.Zero), 1, true)
        ]);
        _monitoring.GetFollowUpHistory(PatientId, Arg.Any<CancellationToken>()).Returns([
            new FollowUpHistoryItem(PractitionerId, new DateTimeOffset(2026, 9, 18, 15, 0, 0, TimeSpan.Zero), "Remote",
                "Completed", new DateTimeOffset(2026, 9, 18, 15, 30, 0, TimeSpan.Zero), null, 2),
            new FollowUpHistoryItem(PractitionerId, new DateTimeOffset(2026, 11, 2, 15, 0, 0, TimeSpan.Zero),
                "InPerson", "Scheduled", null, null, null)
        ]);
        _careRelationship.GetCareLinkHistoryByPatientId(PatientId, Arg.Any<CancellationToken>()).Returns([
            new CareLinkHistoryItem(PractitionerId, "Active", new DateTimeOffset(2026, 3, 10, 12, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 3, 10, 12, 5, 0, TimeSpan.Zero), null, true,
                new DateTimeOffset(2026, 3, 10, 12, 5, 0, TimeSpan.Zero), null, null)
        ]);
    }

    [Fact]
    public async Task The_owner_downloads_a_zip_with_the_six_csv_files()
    {
        var result = await Controller(PatientId).GetDataExport(PatientId);

        var file = Assert.IsType<FileContentResult>(result);
        Assert.Equal("application/zip", file.ContentType);
        Assert.Equal("healthify-mis-datos-2026-10-21.zip", file.FileDownloadName);

        var csv = Unzip(file.FileContents);
        Assert.Equal(ExpectedFiles.Order(), csv.Keys.Order());
        foreach (var bytes in csv.Values)
            Assert.Equal(Encoding.UTF8.GetPreamble(), bytes.Take(3).ToArray()); // UTF-8 with BOM, for Excel
    }

    [Fact]
    public async Task Each_file_carries_the_patients_rows_with_headers_in_their_language()
    {
        var csv = Tables(await Composer().Export(PatientId));

        Assert.Equal(["Nombre", "Correo electrónico", "Idioma", "Fecha de alta"], csv["perfil.csv"][0]);
        Assert.Equal(["Ana Flores", "ana@correo.com", "Español", "2026-03-01T10:00:00+00:00"], csv["perfil.csv"][1]);

        var diary = csv["diario.csv"];
        Assert.Equal("¿En el plan?", diary[0][8]);
        Assert.Equal(["2026-09-18T13:05:00-05:00", "Arroz, \"con\" pollo\ny ensalada", "250", "412.5", "20.1", "55",
            "9.75", "Sí", "No", "Idea de comida"], diary[1]);

        Assert.Equal(["2026-09-19T07:30:00-05:00", "74.2", "Sí", "Sí"], csv["autopesajes.csv"][1]);

        var plan = csv["metas.csv"];
        Assert.Equal(["Versión", "Vigente desde", "Vigente ahora", "Energía (kcal/día)", "Proteínas (g/día)",
            "Carbohidratos (g/día)", "Grasas (g/día)", "Código del motivo del cambio", "Motivo del cambio"], plan[0]);
        Assert.Equal(["1", "2026-03-12T15:00:00+00:00", "No", "1800", "90", "200", "60", "", ""], plan[1]);
        Assert.Equal("1650.5", plan[2][3]);
        Assert.Equal(["NewConsultation", "Nueva consulta del 2026-09-18"], plan[2][7..]);
        Assert.Equal(["Custom", "Ajuste indicado por tu nutricionista"], plan[3][7..]);

        var consultations = csv["consultas.csv"];
        Assert.Equal(4, consultations.Count); // header + first consultation without a visit + two visits
        Assert.Equal(["2026-03-12T15:00:00+00:00", "", "Realizada", "", "2026-03-12T15:00:00+00:00", "1"],
            consultations[1]);
        Assert.Equal(["2026-09-18T15:00:00+00:00", "Virtual", "Realizada", "Rosa Medina",
            "2026-09-18T15:30:00+00:00", "2"], consultations[2]);
        Assert.Equal(["Presencial", "Programada"], consultations[3][1..3]);

        Assert.Equal(["Rosa Medina", "Activo", "2026-03-10T12:00:00+00:00", "2026-03-10T12:05:00+00:00", "", "Sí",
            "2026-03-10T12:05:00+00:00", "", ""], csv["vinculos.csv"][1]);
    }

    [Fact]
    public async Task An_english_speaking_patient_gets_english_headers_and_words()
    {
        GivenLanguage("en");

        var csv = Tables(await Composer().Export(PatientId));

        Assert.Equal(["Name", "Email", "Language", "Registered on"], csv["perfil.csv"][0]);
        Assert.Equal("English", csv["perfil.csv"][1][2]);
        Assert.Equal("In your plan?", csv["diario.csv"][0][8]);
        Assert.Equal("New consultation on 2026-09-18", csv["metas.csv"][2][8]);
        Assert.Equal("Remote", csv["consultas.csv"][2][1]);
    }

    [Fact]
    public async Task Another_user_is_refused_and_nothing_is_read()
    {
        var result = await Controller(99).GetDataExport(PatientId);

        var problem = ProblemResponses.Problem(result);
        Assert.Equal(StatusCodes.Status403Forbidden, problem.Status);
        Assert.Equal(ProblemDetailsErrorCodes.AccessNotAllowed, ProblemResponses.Code(problem));
        await _iam.DidNotReceiveWithAnyArgs().GetAccountProfile(default);
        await _intake.DidNotReceiveWithAnyArgs().GetDiaryExport(default);
    }

    [Fact]
    public void Only_a_patient_session_reaches_the_endpoint()
    {
        // A practitioner, even with an active link, is refused by the role before the action runs (403).
        var authorize = typeof(PatientDataExportController).GetCustomAttribute<AuthorizeAttribute>()!;
        Assert.Equal("Patient", authorize.Roles);
    }

    [Fact]
    public async Task No_file_has_a_diagnosis_rationale_calculation_basis_or_bmi_column()
    {
        string[] forbidden = ["diagn", "imc", "bmi", "rationale", "fundament", "justificaci", "calculation", "cálculo",
            "calculo", "basis", "categor"];

        foreach (var language in new[] { "es", "en" })
        {
            GivenLanguage(language);
            var csv = Tables(await Composer().Export(PatientId));

            foreach (var rows in csv.Values)
            foreach (var header in rows[0])
                Assert.DoesNotContain(forbidden, f => header.Contains(f, StringComparison.OrdinalIgnoreCase));
        }

        // The practitioner-only contracts are never read.
        await _nutritionalCare.DidNotReceiveWithAnyArgs().GetActiveDiagnosis(default);
        await _nutritionalCare.DidNotReceiveWithAnyArgs().GetClinicalEvaluations(default);
        await _nutritionalCare.DidNotReceiveWithAnyArgs().GetBaselineSummary(default);
    }

    [Fact]
    public void The_contracts_of_the_export_carry_nothing_clinical()
    {
        string[] forbidden = ["Diagnosis", "Bmi", "Rationale", "CalculationBasis", "Category", "Statement"];
        Type[] contracts =
        [
            typeof(AccountProfileItem), typeof(DiaryExportEntryItem), typeof(SelfWeighInExportItem),
            typeof(PlanVersionHistoryItem), typeof(FollowUpHistoryItem), typeof(CompletedConsultationItem),
            typeof(CareLinkHistoryItem)
        ];

        foreach (var property in contracts.SelectMany(t => t.GetProperties()))
            Assert.DoesNotContain(forbidden, f => property.Name.Contains(f, StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("plain", "plain")]
    [InlineData("a,b", "\"a,b\"")]
    [InlineData("say \"hi\"", "\"say \"\"hi\"\"\"")]
    [InlineData("two\nlines", "\"two\nlines\"")]
    [InlineData("cr\r\nlf", "\"cr\r\nlf\"")]
    [InlineData("", "")]
    public void Fields_are_escaped_as_rfc_4180(string value, string expected)
    {
        Assert.Equal(expected, Csv.Escape(value));
    }

    [Fact]
    public void A_text_that_looks_like_a_formula_is_neutralised()
    {
        Assert.Equal("'=HYPERLINK(\"x\")", Csv.Text("=HYPERLINK(\"x\")"));
        Assert.Equal("'+1", Csv.Text("+1"));
        Assert.Equal("Pollo", Csv.Text("Pollo"));
    }

    [Fact]
    public void Numbers_use_a_decimal_point_whatever_the_culture()
    {
        var previous = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo("es-PE");
            Assert.Equal("74.25", Csv.Number(74.25m));
            Assert.Equal("1850", Csv.Number(1850.00m));
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public async Task A_facade_that_cannot_answer_leaves_its_file_with_headers_only()
    {
        _intake.GetDiaryExport(PatientId, Arg.Any<CancellationToken>()).Returns([]);
        _iam.GetAccountProfile(PatientId, Arg.Any<CancellationToken>()).Returns((AccountProfileItem?)null);

        var csv = Tables(await Composer().Export(PatientId));

        Assert.Single(csv["diario.csv"]);
        Assert.Single(csv["perfil.csv"]);
        Assert.Equal("Nombre", csv["perfil.csv"][0][0]); // no profile: Spanish by default
    }

    [Fact]
    public async Task The_intake_facade_exports_the_diary_with_the_nutrients_of_each_portion_from_one_catalog_read()
    {
        const int riceId = 41;
        var confirmed = new DiaryEntry(PatientId, new LocalTimestamp(new DateTimeOffset(2026, 9, 18, 13, 0, 0,
            TimeSpan.FromHours(-5))), new Provenance(Provenance.Manual), new SyncState(SyncState.Synced));
        confirmed.ConfirmDirectly(new ConfirmedEstimate(riceId, 200m, Now), new PlanAdherence(PlanAdherence.InPlan));
        var waiting = new DiaryEntry(PatientId, new LocalTimestamp(new DateTimeOffset(2026, 9, 18, 20, 0, 0,
            TimeSpan.FromHours(-5))), new Provenance(Provenance.Photo), new SyncState(SyncState.Synced));
        waiting.ProposeEstimate(new ProposedEstimate(riceId, 100m, new Confidence(0.8m), Now));

        var diary = Substitute.For<IDiaryEntryQueryService>();
        diary.Handle(Arg.Any<GetDiaryEntriesByPatientIdQuery>(), Arg.Any<CancellationToken>())
            .Returns(new[] { waiting, confirmed }.AsEnumerable());
        var catalog = Substitute.For<IFoodCatalogContextFacade>();
        catalog.GetReferenceFoodsByIds(Arg.Any<IReadOnlyList<int>>(), Arg.Any<CancellationToken>())
            .Returns([new ReferenceFoodItem(riceId, "Arroz", 130m, 2.5m, 28m, 0.3m, false)]);
        var weighIns = Substitute.For<ISelfWeighInQueryService>();
        weighIns.Handle(Arg.Any<GetSelfWeighInsByPatientIdQuery>(), Arg.Any<CancellationToken>())
            .Returns(new[] { WeighIns.Reading(1, PatientId, new DateOnly(2026, 9, 19), 74.2m, fasted: false) }
                .AsEnumerable());
        var protocol = Substitute.For<ISelfWeighInProtocolProvider>();
        protocol.Current.Returns(SelfWeighInProtocol.Default);

        var facade = new IntakeContextFacade(diary, Substitute.For<IWeightTrendQueryService>(), catalog, weighIns,
            protocol);
        var entries = await facade.GetDiaryExport(PatientId);
        var readings = await facade.GetSelfWeighIns(PatientId);

        Assert.Equal(2, entries.Count);
        Assert.Equal([200m, 100m], entries.Select(e => e.PortionGrams));
        Assert.Equal(260m, entries[0].EnergyKcal);
        Assert.Equal(56m, entries[0].CarbG);
        Assert.True(entries[0].IsConfirmed);
        Assert.False(entries[1].IsConfirmed); // the proposal travels, and says it is one
        Assert.Equal("Photo", entries[1].Provenance);
        await catalog.Received(1).GetReferenceFoodsByIds(Arg.Any<IReadOnlyList<int>>(), Arg.Any<CancellationToken>());
        await catalog.DidNotReceiveWithAnyArgs().GetReferenceFoodById(default);

        var reading = Assert.Single(readings);
        Assert.Equal(74.2m, reading.ValueKg);
        Assert.False(reading.Fasted);
        Assert.False(reading.FollowsProtocol);
    }

    [Fact]
    public async Task The_intake_facade_degrades_to_an_empty_diary()
    {
        var diary = Substitute.For<IDiaryEntryQueryService>();
        diary.Handle(Arg.Any<GetDiaryEntriesByPatientIdQuery>(), Arg.Any<CancellationToken>())
            .Returns<IEnumerable<DiaryEntry>>(_ => throw new InvalidOperationException("db down"));

        var facade = new IntakeContextFacade(diary, Substitute.For<IWeightTrendQueryService>(), Fakes.Catalog());

        Assert.Empty(await facade.GetDiaryExport(PatientId));
        Assert.Empty(await facade.GetSelfWeighIns(PatientId));
    }

    private void GivenLanguage(string language)
    {
        _iam.GetAccountProfile(PatientId, Arg.Any<CancellationToken>())
            .Returns(new AccountProfileItem(PatientId, "ana@correo.com", "Ana", "Flores", language,
                new DateTimeOffset(2026, 3, 1, 10, 0, 0, TimeSpan.Zero)));
    }

    private PatientDataExportComposer Composer()
    {
        var localizer = new StringLocalizer<DataExportMessages>(new ResourceManagerStringLocalizerFactory(
            Options.Create(new LocalizationOptions()), NullLoggerFactory.Instance));
        return new PatientDataExportComposer(_iam, _intake, _nutritionalCare, _monitoring, _careRelationship,
            new PatientDataExportWriter(localizer, new FixedTimeProvider(Now)));
    }

    private PatientDataExportController Controller(int userId)
    {
        return new PatientDataExportController(Composer(), ProblemResponses.EchoLocalizer<SharedResource>())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [
                            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                            new Claim(ClaimTypes.Role, "Patient")
                        ], "test"))
                }
            }
        };
    }

    private static Dictionary<string, byte[]> Unzip(byte[] zip)
    {
        using var archive = new ZipArchive(new MemoryStream(zip), ZipArchiveMode.Read);
        return archive.Entries.ToDictionary(e => e.FullName, e =>
        {
            using var stream = e.Open();
            using var copy = new MemoryStream();
            stream.CopyTo(copy);
            return copy.ToArray();
        });
    }

    private static Dictionary<string, List<string[]>> Tables(PatientDataExportFile file)
    {
        return Unzip(file.Content).ToDictionary(f => f.Key, f => Parse(Encoding.UTF8.GetString(f.Value)));
    }

    /// <summary>A strict RFC 4180 reader: rows end in CRLF, quoted fields may hold commas, quotes and line breaks.</summary>
    private static List<string[]> Parse(string text)
    {
        if (text.Length > 0 && text[0] == '﻿') text = text[1..];
        var rows = new List<string[]>();
        var row = new List<string>();
        var field = new StringBuilder();
        var quoted = false;

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < text.Length && text[i + 1] == '"')
                {
                    field.Append('"');
                    i++;
                }
                else if (c == '"') quoted = false;
                else field.Append(c);
            }
            else if (c == '"') quoted = true;
            else if (c == ',')
            {
                row.Add(field.ToString());
                field.Clear();
            }
            else if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
            {
                row.Add(field.ToString());
                field.Clear();
                rows.Add(row.ToArray());
                row = [];
                i++;
            }
            else field.Append(c);
        }

        Assert.False(quoted);
        Assert.Empty(row);
        return rows;
    }
}
