using System.Text.Json.Nodes;
using Healthify.Platform.Shared.Application.Ai;

namespace Healthify.Platform.Tests.Ai;

/// <summary>IA-0, guard 3. Nothing that identifies a person leaves the platform; the aggregates do.</summary>
public class AiInputPseudonymizerTests
{
    private static readonly object Input = new
    {
        PatientId = 4711,
        PractitionerId = 815,
        Id = 4711,
        Name = "Ana María Pérez",
        GivenNames = "Ana María",
        FamilyNames = "Pérez",
        Email = "ana.perez@correo.com",
        PhotoUrl = "https://cdn.example.com/p/4711.jpg",
        Week = new
        {
            DaysLogged = 6,
            KcalPerDay = new[] { 1850, 1920, 2010 },
            Meals = new[]
            {
                new { MealId = 99, FoodName = "Avena", Note = "Ana María desayunó con su hija" },
                new { MealId = 100, FoodName = "Ensalada", Note = "me escribió desde ana.perez@correo.com" }
            }
        },
        RelatedPatientIds = new[] { 4711, 4712 },
        patient_id = 4711
    };

    [Fact]
    public void No_name_email_photo_or_real_identifier_survives()
    {
        var output = AiInputPseudonymizer.Pseudonymize(Input, ["Ana María Pérez", "Ana María", "Pérez"]).Json;

        foreach (var leak in new[] { "Ana", "María", "Pérez", "ana.perez", "correo.com", "cdn.example.com", "4711",
                     "4712", "815", "\"99\"", ":99", ":100" })
            Assert.DoesNotContain(leak, output);
    }

    [Fact]
    public void Identifiers_become_references_local_to_the_request_and_consistent_within_it()
    {
        var json = JsonNode.Parse(AiInputPseudonymizer.Pseudonymize(Input).Json)!;

        Assert.Equal("ref-1", json["patientId"]!.GetValue<string>());
        Assert.Equal("ref-1", json["id"]!.GetValue<string>()); // same real value, same reference
        Assert.Equal("ref-1", json["patient_id"]!.GetValue<string>());
        Assert.NotEqual("ref-1", json["practitionerId"]!.GetValue<string>());
        Assert.Equal(["ref-1", "ref-5"], json["relatedPatientIds"]!.AsArray().Select(n => n!.GetValue<string>()));
        Assert.Null(json["name"]);
        Assert.Null(json["email"]);
        Assert.Null(json["photoUrl"]);
    }

    [Fact]
    public void The_aggregates_the_function_needs_stay_as_they_are()
    {
        var json = JsonNode.Parse(AiInputPseudonymizer.Pseudonymize(Input, ["Ana María"]).Json)!;

        Assert.Equal(6, json["week"]!["daysLogged"]!.GetValue<int>());
        Assert.Equal([1850, 1920, 2010], json["week"]!["kcalPerDay"]!.AsArray().Select(n => n!.GetValue<int>()));
        Assert.Equal("Avena", json["week"]!["meals"]![0]!["foodName"]!.GetValue<string>());
        Assert.Equal($"{AiInputPseudonymizer.IdentifierMask} desayunó con su hija",
            json["week"]!["meals"]![0]!["note"]!.GetValue<string>());
        Assert.Equal($"me escribió desde {AiInputPseudonymizer.EmailMask}",
            json["week"]!["meals"]![1]!["note"]!.GetValue<string>());
    }

    [Fact]
    public void Known_identifiers_are_masked_as_whole_words_only()
    {
        var json = JsonNode.Parse(AiInputPseudonymizer.Pseudonymize(
            new { Note = "ana comió banana con Ana" }, ["Ana"]).Json)!;

        Assert.Equal("[redacted] comió banana con [redacted]", json["note"]!.GetValue<string>());
    }

    [Fact]
    public void The_hash_is_the_sha256_of_what_leaves_and_is_stable()
    {
        var first = AiInputPseudonymizer.Pseudonymize(Input);
        var second = AiInputPseudonymizer.Pseudonymize(Input);

        Assert.Equal(first, second);
        Assert.Equal(64, first.Hash.Length);
        Assert.Equal(AiInputPseudonymizer.Sha256(first.Json), first.Hash);
    }
}
