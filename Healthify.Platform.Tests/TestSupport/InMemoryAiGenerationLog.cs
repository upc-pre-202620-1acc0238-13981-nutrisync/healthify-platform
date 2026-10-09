using Healthify.Platform.Shared.Application.Ai;

namespace Healthify.Platform.Tests.TestSupport;

/// <summary>
///     IA-0. <see cref="IAiGenerationLog" /> in memory, with the same counting and purging rules as the EF one
///     (Blocked rows do not count towards the quota; a requester, when given, is counted instead of the patient).
/// </summary>
public sealed class InMemoryAiGenerationLog : IAiGenerationLog
{
    private readonly List<(long Id, AiGenerationRecord Record)> _rows = [];

    public IReadOnlyList<AiGenerationRecord> Rows
    {
        get
        {
            lock (_rows) return _rows.Select(r => r.Record).ToList();
        }
    }

    /// <summary>Makes every RecordAsync throw, to prove an unauditable success is not returned.</summary>
    public bool FailOnRecord { get; set; }

    public Task<long> RecordAsync(AiGenerationRecord record, CancellationToken cancellationToken = default)
    {
        if (FailOnRecord) throw new InvalidOperationException("Simulated audit failure.");
        lock (_rows)
        {
            var id = _rows.Count + 1L;
            _rows.Add((id, record));
            return Task.FromResult(id);
        }
    }

    public Task<int> CountSinceAsync(AiFeature feature, int subjectPatientId, int? requestedByUserId,
        DateTimeOffset since, CancellationToken cancellationToken = default)
    {
        lock (_rows)
            return Task.FromResult(_rows.Count(r =>
                r.Record.Feature == feature.Name && r.Record.CreatedAt >= since &&
                r.Record.Status != AiGenerationStatus.Blocked &&
                (requestedByUserId is { } requester
                    ? r.Record.RequestedByUserId == requester
                    : r.Record.SubjectPatientId == subjectPatientId)));
    }

    public Task<int> PurgeForPatientAsync(int subjectPatientId, AiFeature? feature = null,
        CancellationToken cancellationToken = default)
    {
        lock (_rows)
            return Task.FromResult(_rows.RemoveAll(r =>
                r.Record.SubjectPatientId == subjectPatientId && (feature is null || r.Record.Feature == feature.Name)));
    }

    public Task<int> PurgeExpiredAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        lock (_rows)
            return Task.FromResult(_rows.RemoveAll(r => r.Record.ExpiresAt is { } expires && expires <= now));
    }

    /// <summary>Seeds a past generation, as if the pipeline had recorded it.</summary>
    public void Seed(AiFeature feature, int patientId, AiGenerationStatus status, DateTimeOffset at,
        int? requestedBy = null)
    {
        lock (_rows)
            _rows.Add((_rows.Count + 1L, new AiGenerationRecord(feature.Name, patientId, requestedBy, "seed@1",
                "seed", null, null, status, null, 0, 0, 0, at, at.AddDays(180))));
    }
}
