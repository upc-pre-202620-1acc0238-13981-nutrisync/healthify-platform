using System.Text;
using Healthify.Platform.MonitoringAdherence.Domain.Model.ValueObjects;
using Healthify.Platform.MonitoringAdherence.Domain.Services;

namespace Healthify.Platform.MonitoringAdherence.Infrastructure.Mailing;

/// <summary>
///     The appointment reminder email: subject and HTML with the Healthify brand (green header, warm paper
///     background), in Spanish or English.
/// </summary>
/// <remarks>
///     Pure and deterministic, so it is tested without a mail server. Dates are written by hand instead of through
///     <c>CultureInfo</c> so the text does not depend on the ICU data of the host (containers often run with
///     invariant globalization). Every value that comes from a person is HTML-encoded.
/// </remarks>
public static class FollowUpReminderEmailTemplate
{
    private const string Green = "#3B6E23";
    private const string Paper = "#F6F4EE";
    private const string Line = "#D6D7C7";
    private const string Ink = "#1C1C1A";
    private const string Muted = "#5C5C57";
    private const string WarnBackground = "#FFF1D6";
    private const string WarnInk = "#7A4A00";

    private static readonly string[] DaysEs =
        ["domingo", "lunes", "martes", "miércoles", "jueves", "viernes", "sábado"];

    private static readonly string[] DaysEn =
        ["Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday"];

    private static readonly string[] MonthsEs =
    [
        "enero", "febrero", "marzo", "abril", "mayo", "junio", "julio", "agosto", "septiembre", "octubre",
        "noviembre", "diciembre"
    ];

    private static readonly string[] MonthsEn =
    [
        "January", "February", "March", "April", "May", "June", "July", "August", "September", "October",
        "November", "December"
    ];

    public static (string Subject, string Html) Render(FollowUpReminderMessage message, string? practiceAddress,
        string? videoCallUrl)
    {
        var english = string.Equals(message.Language, "en", StringComparison.OrdinalIgnoreCase);
        var toPractitioner = message.Recipient == ReminderRecipient.Practitioner;
        var remote = message.Modality == ConsultationModality.Remote;

        var date = FormatDate(message.LocalStart, english);
        var time = FormatTime(message.LocalStart, english);
        var when = LeadText(message.LeadMinutes, english);

        var subject = english
            ? toPractitioner
                ? $"{(message.Rescheduled ? "Rescheduled · " : "")}Appointment with {message.PatientName} · {date}, {time}"
                : $"{(message.Rescheduled ? "Rescheduled · " : "")}Reminder: your appointment is {when} · {date}, {time}"
            : toPractitioner
                ? $"{(message.Rescheduled ? "Reprogramada · " : "")}Consulta con {message.PatientName} · {date}, {time}"
                : $"{(message.Rescheduled ? "Reprogramada · " : "")}Recordatorio: tu consulta es {when} · {date}, {time}";

        var otherLabel = toPractitioner ? (english ? "Patient" : "Paciente") : english ? "Practitioner" : "Profesional";
        var otherName = toPractitioner ? message.PatientName : message.PractitionerName;
        var modality = remote
            ? english ? "Remote (video call)" : "Remota (videollamada)"
            : english ? "In person" : "Presencial";

        var html = new StringBuilder();
        html.Append("<!DOCTYPE html><html><head><meta charset=\"utf-8\"><meta name=\"viewport\" ")
            .Append("content=\"width=device-width,initial-scale=1\"></head>");
        html.Append($"<body style=\"margin:0;padding:0;background:{Paper};font-family:Arial,Helvetica,sans-serif;color:{Ink}\">");
        html.Append($"<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" style=\"background:{Paper}\"><tr><td align=\"center\" style=\"padding:24px 12px\">");
        html.Append("<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" style=\"max-width:520px\">");

        // Brand header
        html.Append($"<tr><td style=\"background:{Green};padding:18px 24px;border-radius:16px 16px 0 0\">");
        html.Append("<span style=\"font-size:22px;font-weight:700;letter-spacing:.5px;color:#ffffff\">Healthify</span></td></tr>");

        html.Append($"<tr><td style=\"background:#ffffff;padding:24px;border:1px solid {Line};border-top:0;border-radius:0 0 16px 16px\">");

        if (message.Rescheduled)
            html.Append($"<p style=\"margin:0 0 16px;padding:12px 14px;background:{WarnBackground};color:{WarnInk};border-radius:12px;font-size:14px;font-weight:700\">")
                .Append(Encode(english
                    ? "This appointment was rescheduled. This is the new time."
                    : "Esta consulta fue reprogramada. Este es el nuevo horario."))
                .Append("</p>");

        var greeting = string.IsNullOrWhiteSpace(message.RecipientName)
            ? english ? "Hello," : "Hola,"
            : english ? $"Hello {message.RecipientName}," : $"Hola {message.RecipientName},";
        html.Append($"<p style=\"margin:0 0 4px;font-size:16px\">{Encode(greeting)}</p>");
        var intro = toPractitioner
            ? english
                ? $"You have an appointment with {message.PatientName} {when}."
                : $"Tienes una consulta con {message.PatientName} {when}."
            : english
                ? $"This is a reminder of your appointment {when}."
                : $"Te recordamos tu consulta {when}.";
        html.Append($"<p style=\"margin:0 0 20px;font-size:16px;color:{Muted}\">{Encode(intro)}</p>");

        // Date and time
        html.Append($"<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" style=\"background:{Paper};border-radius:12px\"><tr><td style=\"padding:16px 18px\">");
        html.Append($"<div style=\"font-size:20px;font-weight:700;color:{Green}\">{Encode(Capitalize(date))}</div>");
        html.Append($"<div style=\"font-size:32px;font-weight:700;line-height:1.2;margin-top:2px\">{Encode(time)}</div>");
        html.Append("</td></tr></table>");

        // Details
        html.Append("<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" style=\"margin-top:16px\">");
        AppendRow(html, otherLabel, otherName);
        AppendRow(html, english ? "Type" : "Modalidad", modality);
        if (remote && IsHttps(videoCallUrl))
            AppendLinkRow(html, english ? "Link" : "Enlace", videoCallUrl!);
        else if (!remote && !string.IsNullOrWhiteSpace(practiceAddress))
            AppendRow(html, english ? "Address" : "Dirección", practiceAddress);
        html.Append("</table>");

        // Preparation (only for the patient: it is what they have to do)
        var preparation = message.Preparation.Select(code => PreparationText(code, english))
            .Where(text => text is not null).ToList();
        if (!toPractitioner && preparation.Count > 0)
        {
            html.Append($"<p style=\"margin:20px 0 6px;font-size:14px;font-weight:700\">{Encode(english ? "How to prepare" : "Cómo prepararte")}</p>");
            html.Append("<ul style=\"margin:0;padding-left:20px;font-size:14px;line-height:1.6\">");
            foreach (var item in preparation) html.Append($"<li>{Encode(item!)}</li>");
            html.Append("</ul>");
        }

        html.Append($"<p style=\"margin:24px 0 0;padding-top:16px;border-top:1px solid {Line};font-size:12px;color:{Muted}\">");
        html.Append(Encode(english
            ? "Automatic reminder from Healthify. To change or cancel the appointment, talk to your nutritionist."
            : "Recordatorio automático de Healthify. Para cambiar o cancelar la consulta, habla con tu nutricionista."));
        html.Append("</p></td></tr></table></td></tr></table></body></html>");

        return (subject, html.ToString());
    }

    internal static string FormatDate(DateTimeOffset local, bool english)
    {
        return english
            ? $"{DaysEn[(int)local.DayOfWeek]}, {MonthsEn[local.Month - 1]} {local.Day}"
            : $"{DaysEs[(int)local.DayOfWeek]} {local.Day} de {MonthsEs[local.Month - 1]}";
    }

    internal static string FormatTime(DateTimeOffset local, bool english)
    {
        if (!english) return $"{local.Hour:00}:{local.Minute:00}";
        var hour12 = local.Hour % 12 == 0 ? 12 : local.Hour % 12;
        return $"{hour12}:{local.Minute:00} {(local.Hour < 12 ? "AM" : "PM")}";
    }

    /// <summary>«tomorrow», «in 1 hour», «in 30 minutes»…</summary>
    public static string LeadText(int leadMinutes, bool english)
    {
        if (leadMinutes >= 1440)
        {
            var days = leadMinutes / 1440;
            if (days == 1) return english ? "tomorrow" : "mañana";
            return english ? $"in {days} days" : $"en {days} días";
        }

        if (leadMinutes >= 60)
        {
            var hours = leadMinutes / 60;
            return english ? $"in {hours} {(hours == 1 ? "hour" : "hours")}" : $"en {hours} {(hours == 1 ? "hora" : "horas")}";
        }

        return english ? $"in {leadMinutes} minutes" : $"en {leadMinutes} minutos";
    }

    private static string? PreparationText(string code, bool english)
    {
        return code switch
        {
            PreparationInstruction.Fasting => english ? "Come fasting" : "Ven en ayunas",
            PreparationInstruction.LightClothing => english ? "Wear light clothing" : "Usa ropa ligera",
            PreparationInstruction.BringBloodTests => english ? "Bring your blood test results" : "Trae tus exámenes de sangre",
            PreparationInstruction.EmptyBladder => english ? "Empty your bladder before the visit" : "Vacía la vejiga antes de la consulta",
            _ => null
        };
    }

    private static void AppendRow(StringBuilder html, string label, string value)
    {
        html.Append($"<tr><td style=\"padding:6px 0;font-size:13px;color:{Muted};width:110px;vertical-align:top\">{Encode(label)}</td>");
        html.Append($"<td style=\"padding:6px 0;font-size:15px;font-weight:600\">{Encode(value)}</td></tr>");
    }

    private static void AppendLinkRow(StringBuilder html, string label, string url)
    {
        html.Append($"<tr><td style=\"padding:6px 0;font-size:13px;color:{Muted};width:110px;vertical-align:top\">{Encode(label)}</td>");
        html.Append($"<td style=\"padding:6px 0;font-size:15px;font-weight:600\"><a href=\"{Encode(url)}\" style=\"color:{Green}\">{Encode(url)}</a></td></tr>");
    }

    private static bool IsHttps(string? url)
    {
        return !string.IsNullOrWhiteSpace(url) && Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
               uri.Scheme == Uri.UriSchemeHttps;
    }

    private static string Capitalize(string text)
    {
        return text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];
    }

    /// <summary>
    ///     Escapes the five characters that can break out of HTML text or an attribute. Accented letters stay as
    ///     they are: the message is sent as UTF-8, and <c>WebUtility.HtmlEncode</c> would turn every
    ///     «ó» into a numeric entity.
    /// </summary>
    private static string Encode(string value)
    {
        return value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;")
            .Replace("'", "&#39;");
    }
}
