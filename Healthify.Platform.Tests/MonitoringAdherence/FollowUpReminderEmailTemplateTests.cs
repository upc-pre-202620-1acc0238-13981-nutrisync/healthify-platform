using Healthify.Platform.MonitoringAdherence.Domain.Model.ValueObjects;
using Healthify.Platform.MonitoringAdherence.Domain.Services;
using Healthify.Platform.MonitoringAdherence.Infrastructure.Clock;
using Healthify.Platform.MonitoringAdherence.Infrastructure.Mailing;

namespace Healthify.Platform.Tests.MonitoringAdherence;

/// <summary>The reminder email: wording, brand, time zone, preparation, links and escaping.</summary>
public class FollowUpReminderEmailTemplateTests
{
    private static readonly TimeZoneInfo Lima =
        TimeZoneInfo.CreateCustomTimeZone("practice", TimeSpan.FromHours(-5), "practice", "practice");

    // 2026-11-20 15:00 UTC is 10:00 in Lima: a Friday.
    private static readonly DateTimeOffset Utc = new(2026, 11, 20, 15, 0, 0, TimeSpan.Zero);

    private static FollowUpReminderMessage Message(string recipient = ReminderRecipient.Patient,
        string language = "es", int lead = 1440, bool rescheduled = false,
        string modality = ConsultationModality.InPerson, string[]? preparation = null,
        string patient = "Ana Flores", string practitioner = "Marta Quispe", string name = "Ana Flores",
        DateTimeOffset? start = null)
    {
        return new FollowUpReminderMessage(recipient, "ana@example.com", name, language, patient, practitioner,
            start ?? new ClinicalTimeZoneFollowUpCalendar(Lima).ToLocal(Utc), modality,
            preparation ?? [], lead, rescheduled);
    }

    [Fact]
    public void The_time_is_written_in_the_time_zone_of_the_practice_not_in_utc()
    {
        var (subject, html) = FollowUpReminderEmailTemplate.Render(Message(), null, null);

        Assert.Contains("10:00", html);
        Assert.DoesNotContain("15:00", html);
        Assert.Contains("viernes 20 de noviembre", subject);
        Assert.Contains("Viernes 20 de noviembre", html);
    }

    [Fact]
    public void A_visit_late_in_utc_falls_on_the_previous_day_in_the_practice()
    {
        var calendar = new ClinicalTimeZoneFollowUpCalendar(Lima);
        var local = calendar.ToLocal(new DateTimeOffset(2026, 11, 21, 3, 30, 0, TimeSpan.Zero));

        var (subject, html) = FollowUpReminderEmailTemplate.Render(Message(start: local), null, null);

        Assert.Contains("viernes 20 de noviembre", subject);
        Assert.Contains("22:30", html);
    }

    [Fact]
    public void The_patient_gets_a_day_before_reminder_in_spanish_with_the_brand()
    {
        var (subject, html) = FollowUpReminderEmailTemplate.Render(Message(), null, null);

        Assert.Equal("Recordatorio: tu consulta es mañana · viernes 20 de noviembre, 10:00", subject);
        Assert.Contains("Healthify", html);
        Assert.Contains("#3B6E23", html);
        Assert.Contains("Hola Ana Flores,", html);
        Assert.Contains("Te recordamos tu consulta mañana.", html);
        Assert.Contains("Profesional", html);
        Assert.Contains("Marta Quispe", html);
        Assert.Contains("Presencial", html);
        Assert.Contains("<html", html);
    }

    [Fact]
    public void The_same_reminder_in_english_uses_a_12_hour_clock()
    {
        var local = new ClinicalTimeZoneFollowUpCalendar(Lima).ToLocal(new DateTimeOffset(2026, 11, 20, 20, 5, 0,
            TimeSpan.Zero)); // 15:05 in Lima

        var (subject, html) = FollowUpReminderEmailTemplate.Render(
            Message(language: "en", lead: 60, start: local), null, null);

        Assert.Equal("Reminder: your appointment is in 1 hour · Friday, November 20, 3:05 PM", subject);
        Assert.Contains("Hello Ana Flores,", html);
        Assert.Contains("In person", html);
        Assert.Contains("Practitioner", html);
    }

    [Theory]
    [InlineData(1440, "mañana", "tomorrow")]
    [InlineData(2880, "en 2 días", "in 2 days")]
    [InlineData(60, "en 1 hora", "in 1 hour")]
    [InlineData(180, "en 3 horas", "in 3 hours")]
    [InlineData(30, "en 30 minutos", "in 30 minutes")]
    public void The_lead_time_is_written_in_plain_words(int minutes, string spanish, string english)
    {
        Assert.Equal(spanish, FollowUpReminderEmailTemplate.LeadText(minutes, false));
        Assert.Equal(english, FollowUpReminderEmailTemplate.LeadText(minutes, true));
    }

    [Fact]
    public void A_rescheduled_visit_says_so_in_the_subject_and_the_body()
    {
        var (subject, html) = FollowUpReminderEmailTemplate.Render(Message(rescheduled: true), null, null);

        Assert.StartsWith("Reprogramada · ", subject);
        Assert.Contains("Esta consulta fue reprogramada. Este es el nuevo horario.", html);

        var (plainSubject, plainHtml) = FollowUpReminderEmailTemplate.Render(Message(), null, null);
        Assert.DoesNotContain("Reprogramada", plainSubject);
        Assert.DoesNotContain("reprogramada", plainHtml);
    }

    [Fact]
    public void An_in_person_visit_shows_the_address_when_there_is_one()
    {
        var (_, withAddress) = FollowUpReminderEmailTemplate.Render(Message(), "Av. Primavera 123, Surco",
            "https://meet.example.com/abc");
        var (_, withoutAddress) = FollowUpReminderEmailTemplate.Render(Message(), null, null);

        Assert.Contains("Dirección", withAddress);
        Assert.Contains("Av. Primavera 123, Surco", withAddress);
        Assert.DoesNotContain("meet.example.com", withAddress);
        Assert.DoesNotContain("Dirección", withoutAddress);
    }

    [Fact]
    public void A_remote_visit_shows_the_link_only_when_it_is_https()
    {
        var remote = Message(modality: ConsultationModality.Remote);

        var (_, ok) = FollowUpReminderEmailTemplate.Render(remote, "Av. Primavera 123", "https://meet.example.com/abc");
        var (_, plain) = FollowUpReminderEmailTemplate.Render(remote, "Av. Primavera 123", "http://meet.example.com/abc");
        var (_, script) = FollowUpReminderEmailTemplate.Render(remote, null, "javascript:alert(1)");

        Assert.Contains("Remota (videollamada)", ok);
        Assert.Contains("href=\"https://meet.example.com/abc\"", ok);
        Assert.DoesNotContain("Av. Primavera", ok);
        Assert.DoesNotContain("meet.example.com", plain);
        Assert.DoesNotContain("javascript", script);
    }

    [Fact]
    public void Preparation_is_listed_for_the_patient_but_not_for_the_practitioner()
    {
        var preparation = new[] { PreparationInstruction.Fasting, PreparationInstruction.BringBloodTests };

        var (_, patientHtml) = FollowUpReminderEmailTemplate.Render(Message(preparation: preparation), null, null);
        var (_, practitionerHtml) = FollowUpReminderEmailTemplate.Render(
            Message(ReminderRecipient.Practitioner, preparation: preparation), null, null);

        Assert.Contains("Cómo prepararte", patientHtml);
        Assert.Contains("Ven en ayunas", patientHtml);
        Assert.Contains("Trae tus exámenes de sangre", patientHtml);
        Assert.DoesNotContain("Cómo prepararte", practitionerHtml);
    }

    [Fact]
    public void Without_preparation_there_is_no_preparation_block()
    {
        var (_, html) = FollowUpReminderEmailTemplate.Render(Message(), null, null);

        Assert.DoesNotContain("Cómo prepararte", html);
    }

    [Fact]
    public void The_practitioner_reminder_names_the_patient()
    {
        var (subject, html) = FollowUpReminderEmailTemplate.Render(
            Message(ReminderRecipient.Practitioner, name: "Marta Quispe"), null, null);

        Assert.Equal("Consulta con Ana Flores · viernes 20 de noviembre, 10:00", subject);
        Assert.Contains("Tienes una consulta con Ana Flores mañana.", html);
        Assert.Contains("Paciente", html);
        Assert.Contains("Hola Marta Quispe,", html);
    }

    [Fact]
    public void Names_and_addresses_are_html_encoded()
    {
        var hostile = "<script>alert('x')</script>";

        var (_, html) = FollowUpReminderEmailTemplate.Render(
            Message(patient: hostile, practitioner: hostile, name: hostile), hostile, null);

        Assert.DoesNotContain("<script>", html);
        Assert.Contains("&lt;script&gt;", html);
    }

    [Fact]
    public void An_unknown_language_falls_back_to_spanish()
    {
        var (subject, _) = FollowUpReminderEmailTemplate.Render(Message(language: "fr"), null, null);

        Assert.StartsWith("Recordatorio:", subject);
    }

    [Fact]
    public void Dates_do_not_depend_on_the_host_culture()
    {
        var previous = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
            var (subject, _) = FollowUpReminderEmailTemplate.Render(Message(), null, null);

            Assert.Contains("viernes 20 de noviembre, 10:00", subject);
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = previous;
        }
    }
}
