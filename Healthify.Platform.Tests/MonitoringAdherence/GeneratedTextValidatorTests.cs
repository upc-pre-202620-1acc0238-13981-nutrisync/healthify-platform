using Healthify.Platform.MonitoringAdherence.Application.Internal;
using Healthify.Platform.MonitoringAdherence.Domain.Model.ValueObjects;
using Healthify.Platform.MonitoringAdherence.Domain.Services;
using Healthify.Platform.MonitoringAdherence.Infrastructure.Ai.Lexicon;
using Healthify.Platform.Tests.TestSupport;

namespace Healthify.Platform.Tests.MonitoringAdherence;

/// <summary>
///     IA-2/IA-4/IA-5, guard 6. The validators of this context reject a text whose numbers are not the facts of its
///     period (The AI Does Not Count), a text that accuses (Invitation Tone Never Accusation, lexicon es/en of the
///     context), a patient text that mentions a diagnosis, BMI or calculation basis, and a practitioner text that
///     mentions the consistency index before an escalation (Patient Shown First).
/// </summary>
public class GeneratedTextValidatorTests
{
    private static readonly IAiLanguageLexicon Lexicon = EmbeddedAiLanguageLexicon.Instance;

    /// <summary>Met 5, Short 1 (Thursday), Unlogged 1 (Sunday); weight −0.3 kg; 4 weigh-ins.</summary>
    private static MonitoringPeriodFacts TheWeek()
    {
        var scenario = new MonitoringAiScenario();
        scenario.LogTheWeek();
        return MonitoringPeriodFacts.Compute(MonitoringAiScenario.WeekStart, MonitoringAiScenario.WeekStart.AddDays(6),
            scenario.Evaluated,
            scenario.Moments.GroupBy(m => m.Date)
                .ToDictionary(g => g.Key, g => (IReadOnlyList<DateTimeOffset>)g.Select(m => m.LocalTimestamp).ToList()),
            -0.3m, -0.3m, 4);
    }

    private static WeeklySummaryOutput Summary(string headline, string[]? wentWell = null, string[]? watchOut = null)
    {
        return new WeeklySummaryOutput(headline,
            wentWell ?? ["Registraste tus comidas 6 de 7 días.", "Tu tendencia de peso bajó 0,3 kg."],
            watchOut ?? ["El jueves podrías sumar algo más en el almuerzo."]);
    }

    private static IReadOnlyList<string> Validate(WeeklySummaryOutput output)
    {
        return new WeeklySummaryOutputValidator(TheWeek(), Lexicon).Validate(output);
    }

    [Fact]
    public void A_summary_whose_numbers_are_the_facts_is_accepted()
    {
        Assert.Empty(Validate(Summary("Cumpliste tus metas 5 de 7 días.")));
        Assert.Empty(Validate(Summary("You met your targets 5 of 7 days.",
            ["You logged your meals 6 out of 7 days.", "Your weight trend went down 0.3 kg."],
            ["On Thursday you could add a little more at lunch."])));
    }

    [Theory]
    [InlineData("Cumpliste tus metas 4 de 7 días.")] // 4 is a count (weigh-ins), but not a count of days
    [InlineData("Cumpliste tus metas 5 de 6 días.")] // the week has 7 days
    [InlineData("Cumpliste el 71 % de tus metas.")] // a percentage the facts do not have
    [InlineData("Cumpliste tus metas cuatro de siete días.")] // spelled numbers are checked too
    [InlineData("Esta semana sumaste 12.600 kcal.")]
    public void A_number_that_is_not_a_fact_rejects_the_summary(string headline)
    {
        var violations = Validate(Summary(headline));

        Assert.Contains(violations,
            v => v.StartsWith("headline:") && (v.Contains("number") || v.Contains("days of the period")));
    }

    [Fact]
    public void An_invented_weight_change_rejects_the_summary()
    {
        var violations = Validate(Summary("Cumpliste tus metas 5 de 7 días.",
            ["Tu peso bajó 1,2 kg esta semana."]));

        Assert.Contains(violations, v => v.StartsWith("wentWell[0]:") && v.Contains("1,2"));
    }

    [Theory]
    [InlineData("Fallaste el jueves, pero cumpliste 5 de 7 días.")]
    [InlineData("Te pasaste el fin de semana.")]
    [InlineData("El jueves comiste MAL.")]
    [InlineData("No cumpliste tu plan el domingo.")]
    [InlineData("You failed on Thursday.")]
    [InlineData("You should have logged on Sunday.")]
    [InlineData("Sin excusas: registra el domingo.")]
    public void An_accusatory_text_rejects_the_summary(string watchOut)
    {
        var violations = Validate(Summary("Cumpliste tus metas 5 de 7 días.", watchOut: [watchOut]));

        Assert.Contains(violations, v => v.StartsWith("watchOut[0]:") && v.Contains("accusatory"));
    }

    [Theory]
    [InlineData("Tu semana fue normal y constante.")] // "mal" inside "normal" is not the word "mal"
    [InlineData("Un día sin registro no es un mal día... o sí? No: es solo un dato que falta.")]
    public void Whole_words_only_are_matched(string text)
    {
        var violations = Validate(Summary("Cumpliste tus metas 5 de 7 días.", [text]));

        if (text.Contains("normal")) Assert.Empty(violations);
        else Assert.Contains(violations, v => v.Contains("'mal'"));
    }

    [Theory]
    [InlineData("Vas bien para salir del sobrepeso.")]
    [InlineData("Tu IMC está mejorando.")]
    [InlineData("Tu diagnóstico de obesidad no cambia.")]
    [InlineData("Your BMI is improving.")]
    [InlineData("Tu metabolismo basal necesita más energía.")]
    public void A_patient_summary_that_mentions_a_diagnosis_is_rejected(string text)
    {
        var violations = Validate(Summary("Cumpliste tus metas 5 de 7 días.", [text]));

        Assert.Contains(violations, v => v.StartsWith("wentWell[0]:") && v.Contains("diagnosis"));
    }

    [Fact]
    public void Suggested_questions_must_be_three_to_five_questions_without_diagnosis_or_accusation()
    {
        var validator = new SuggestedQuestionsOutputValidator(TheWeek(), Lexicon);

        Assert.Empty(validator.Validate(new SuggestedQuestionsOutput([
            "¿Qué puedo almorzar los días de trabajo?", "¿Cómo armo una cena más completa?",
            "¿Qué hago los fines de semana cuando como fuera?"
        ])));

        Assert.Contains(validator.Validate(new SuggestedQuestionsOutput(["¿Qué almuerzo?", "¿Qué ceno hoy?"])),
            v => v.StartsWith("questions:"));
        Assert.Contains(validator.Validate(new SuggestedQuestionsOutput([
                "Debería almorzar más.", "¿Cómo armo una cena más completa?", "¿Qué hago los fines de semana?"
            ])),
            v => v.Contains("not a question"));
        Assert.Contains(validator.Validate(new SuggestedQuestionsOutput([
                "¿Qué como para bajar mi obesidad?", "¿Cómo armo una cena más completa?",
                "¿Qué hago los fines de semana?"
            ])),
            v => v.Contains("diagnosis"));
        Assert.Contains(validator.Validate(new SuggestedQuestionsOutput([
                "¿Por qué fallé el jueves?", "¿Cómo armo una cena más completa?", "¿Qué hago los fines de semana?"
            ])),
            v => v.Contains("accusatory"));
        Assert.Contains(validator.Validate(new SuggestedQuestionsOutput([
                "¿Qué hago los 3 días que no almuerzo?", "¿Cómo armo una cena más completa?",
                "¿Qué hago los fines de semana?"
            ])),
            v => v.Contains("number 3"));
    }

    [Fact]
    public void The_practitioner_summary_says_nothing_of_the_consistency_index_before_an_escalation()
    {
        const string text = "Cumple sus metas 5 de 7 días. El índice de consistencia está en alerta.";

        var before = new MonitoringSummaryOutputValidator(TheWeek(), Lexicon, false)
            .Validate(new MonitoringSummaryOutput(text));
        var after = new MonitoringSummaryOutputValidator(TheWeek(), Lexicon, true)
            .Validate(new MonitoringSummaryOutput(text));

        Assert.Contains(before, v => v.Contains("consistency index"));
        Assert.Empty(after);
    }

    [Fact]
    public void The_practitioner_summary_is_professional_but_still_never_accusatory_nor_invents_figures()
    {
        var validator = new MonitoringSummaryOutputValidator(TheWeek(), Lexicon, false);

        Assert.Empty(validator.Validate(new MonitoringSummaryOutput(
            "Cumple sus metas casi todos los días (5 de 7). El jueves registró menos energía de la indicada; el " +
            "domingo, sin registro.")));
        Assert.Contains(validator.Validate(new MonitoringSummaryOutput("Incumplimiento el jueves y el domingo.")),
            v => v.Contains("accusatory"));
        Assert.Contains(validator.Validate(new MonitoringSummaryOutput("Cumple sus metas 3 de 7 días.")),
            v => v.Contains("days of the period"));
    }

    [Fact]
    public void The_lexicon_of_the_context_is_embedded_with_both_languages()
    {
        Assert.Contains("fallaste", Lexicon.AccusatoryTerms);
        Assert.Contains("failed", Lexicon.AccusatoryTerms);
        Assert.Contains("sobrepeso", Lexicon.DiagnosisTerms);
        Assert.Contains("bmi", Lexicon.DiagnosisTerms);
        Assert.Contains("consistency index", Lexicon.ConsistencyTerms);
    }
}
