using Healthify.Platform.MonitoringAdherence.Domain.Model.Commands;
using Healthify.Platform.MonitoringAdherence.Domain.Model.ValueObjects;

namespace Healthify.Platform.MonitoringAdherence.Domain.Model.Aggregates;

/// <summary>
///     MA-4. What the patient tells their practitioner before a visit (PT25.2, PT25.3; EV-2 "Antes de la consulta,
///     … contó").
/// </summary>
/// <remarks>
///     It lives here because it is tied to a <see cref="ScheduledFollowUp" /> and to the period between visits: it
///     is not the diary (Intake) and not a clinical act (Nutritional Care).
///     Business rule: Check In Raises No Signal (MA-4). The check in is voluntary. Nothing reads it to raise a
///     signal, it never enters the consistency index nor a deviation, and "Hard" does not escalate. The event it
///     publishes has no subscriber anywhere.
///     Business rule: Only The Patient Writes (MA-4). Business rule: One Check In Per Visit (MA-4), backed by a
///     unique index on follow_up_id.
/// </remarks>
public partial class PreVisitCheckIn
{
    /// <summary>Rule: at most three questions written by the patient, and at most three accepted suggestions.</summary>
    public const int MaximumQuestionsPerOrigin = 3;

    private List<string> _difficulties = [];
    private List<PatientQuestion> _questions = [];

    /// <summary>Required by EF Core.</summary>
    protected PreVisitCheckIn()
    {
    }

    /// <summary>MA-4 - Submit Pre Visit Check In, the first time.</summary>
    /// <param name="command">What the patient answered.</param>
    /// <param name="followUp">The visit it is for.</param>
    /// <param name="now">The moment it is sent.</param>
    /// <exception cref="ArgumentException">When an answer is invalid.</exception>
    /// <exception cref="InvalidOperationException">When the visit is not this patient's, or no longer accepts it.</exception>
    public PreVisitCheckIn(SubmitPreVisitCheckInCommand command, ScheduledFollowUp followUp, DateTimeOffset now)
    {
        EnsureWritable(command.PatientId, followUp, now);

        FollowUpId = followUp.Id.Value;
        PatientId = followUp.PatientId;
        PractitionerId = followUp.PractitionerId;
        Apply(command);
        SubmittedAt = now;
    }

    public PreVisitCheckInId Id { get; private set; } = null!;

    /// <summary>The visit it is for. One check in per visit.</summary>
    public int FollowUpId { get; private set; }

    /// <summary>Cross-context reference to the patient. A plain int, no EF navigation.</summary>
    public int PatientId { get; private set; }

    /// <summary>Cross-context reference to the practitioner who reads it. A plain int.</summary>
    public int PractitionerId { get; private set; }

    /// <summary>"¿Cómo te sentiste con tu plan?". Required.</summary>
    public PlanFeeling Feeling { get; private set; } = null!;

    /// <summary>"¿Qué te costó más?". May be empty.</summary>
    public IReadOnlyCollection<CheckInDifficulty> Difficulties =>
        _difficulties.Select(d => new CheckInDifficulty(d)).ToList();

    /// <summary>"¿Algo que quieras preguntarle?", with the accepted AI suggestions. May be empty.</summary>
    public IReadOnlyList<PatientQuestion> Questions => _questions.ToList();

    /// <summary>"Enviado el 15 de septiembre": when it was first sent.</summary>
    public DateTimeOffset SubmittedAt { get; private set; }

    /// <summary>When it was last edited ("Editar mi respuesta"); null when never.</summary>
    public DateTimeOffset? EditedAt { get; private set; }

    /// <summary>MA-4 - Edit the check in ("Editar mi respuesta"), until the hour of the visit.</summary>
    /// <exception cref="ArgumentException">When an answer is invalid.</exception>
    /// <exception cref="InvalidOperationException">When the visit is not this one, or no longer accepts it.</exception>
    public void Edit(SubmitPreVisitCheckInCommand command, ScheduledFollowUp followUp, DateTimeOffset now)
    {
        if (followUp.Id.Value != FollowUpId)
            throw new InvalidOperationException("A check in is edited against its own visit.");
        EnsureWritable(command.PatientId, followUp, now);

        Apply(command);
        EditedAt = now;
    }

    /// <summary>
    ///     Rule: Check In Editable Until The Visit. Locked once the visit is completed, cancelled or missed, or its
    ///     hour has arrived.
    /// </summary>
    public static bool IsLockedFor(ScheduledFollowUp followUp, DateTimeOffset now)
    {
        return !followUp.AcceptsCheckInAt(now);
    }

    /// <summary>True when there are more questions of one origin than the rule allows.</summary>
    public static bool ExceedsQuestionLimit(IEnumerable<PatientQuestion> questions)
    {
        return questions.GroupBy(q => q.Origin).Any(g => g.Count() > MaximumQuestionsPerOrigin);
    }

    /// <summary>The questions of a command as value objects, duplicates removed, in the order given.</summary>
    /// <exception cref="ArgumentException">When a question is too short, too long or of an unknown origin.</exception>
    public static IReadOnlyList<PatientQuestion> QuestionsOf(IEnumerable<PreVisitCheckInQuestionInput>? inputs)
    {
        return (inputs ?? [])
            .Select(q => new PatientQuestion(q.Text, q.Origin, q.AiGenerationId, q.Language))
            .Distinct()
            .ToList();
    }

    private static void EnsureWritable(int patientId, ScheduledFollowUp followUp, DateTimeOffset now)
    {
        // Business rule: Only The Patient Writes (MA-4).
        if (followUp.PatientId != patientId)
            throw new InvalidOperationException("Only the patient of the visit writes its check in.");
        // Business rule: Check In Editable Until The Visit (MA-4).
        if (!followUp.AcceptsCheckInAt(now))
            throw new InvalidOperationException("The check in of a visit closes at the hour of the visit.");
    }

    private void Apply(SubmitPreVisitCheckInCommand command)
    {
        var feeling = new PlanFeeling(command.Feeling);
        var difficulties = CheckInDifficulty.ListOf(command.Difficulties);
        var questions = QuestionsOf(command.Questions);
        if (ExceedsQuestionLimit(questions))
            throw new ArgumentException(
                $"A check in has at most {MaximumQuestionsPerOrigin} questions of each origin.", nameof(command));

        Feeling = feeling;
        _difficulties = difficulties.Select(d => d.Value).ToList();
        _questions = questions.ToList();
    }
}
