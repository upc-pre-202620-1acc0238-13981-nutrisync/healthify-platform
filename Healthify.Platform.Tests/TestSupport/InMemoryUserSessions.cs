using System.Reflection;
using Healthify.Platform.Iam.Domain.Model.Aggregates;
using Healthify.Platform.Iam.Domain.Model.ValueObjects;
using Healthify.Platform.Iam.Domain.Repositories;
using Healthify.Platform.Shared.Domain.Repositories;

namespace Healthify.Platform.Tests.TestSupport;

/// <summary>
///     IAM-4. The <c>user_sessions</c> table as EF Core sees it from several requests at once: each request reads
///     its own copy of a row, and a save is conditional on the refresh token hash it read (the concurrency token),
///     failing with <see cref="ConcurrencyConflictException" /> when another request changed it first.
/// </summary>
public sealed class InMemoryUserSessions
{
    private readonly Dictionary<int, UserSession> _rows = [];
    private int _nextId = 30;

    public UserSession Row(int id)
    {
        return Copy(_rows[id]);
    }

    public IReadOnlyCollection<UserSession> Rows => _rows.Values.Select(Copy).ToList();

    /// <summary>A repository and unit of work for one request.</summary>
    public (IUserSessionRepository Repository, IUnitOfWork UnitOfWork) Request(
        Func<Task>? afterFirstLookup = null)
    {
        var request = new SessionRequest(this, afterFirstLookup);
        return (request, request);
    }

    private static UserSession Copy(UserSession source)
    {
        var copy = (UserSession)typeof(object)
            .GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(source, null)!;
        return copy;
    }

    private static void CopyInto(UserSession source, UserSession target)
    {
        for (var type = typeof(UserSession); type is not null && type != typeof(object); type = type.BaseType)
            foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.NonPublic |
                                                 BindingFlags.Public | BindingFlags.DeclaredOnly))
                field.SetValue(target, field.GetValue(source));
    }

    private sealed class SessionRequest(InMemoryUserSessions table, Func<Task>? afterFirstLookup)
        : IUserSessionRepository, IUnitOfWork
    {
        // What this request read of each row: the instance it handed out and the hash it saw.
        private readonly Dictionary<UserSession, string?> _read = [];
        private readonly List<UserSession> _added = [];
        private Func<Task>? _pause = afterFirstLookup;

        public async Task<UserSession?> FindByRefreshTokenHashAsync(string tokenHash,
            CancellationToken cancellationToken = default)
        {
            return await Lookup(s => s.RefreshTokenHash == tokenHash);
        }

        public async Task<UserSession?> FindByPreviousRefreshTokenHashAsync(string tokenHash,
            CancellationToken cancellationToken = default)
        {
            return await Lookup(s => s.PreviousRefreshTokenHash == tokenHash);
        }

        public async Task<UserSession?> FindByIdAsync(int id, CancellationToken cancellationToken = default)
        {
            return await Lookup(s => s.Id.Value == id);
        }

        public Task ReloadAsync(UserSession session, CancellationToken cancellationToken = default)
        {
            var row = table._rows[session.Id.Value];
            CopyInto(row, session);
            _read[session] = row.RefreshTokenHash;
            return Task.CompletedTask;
        }

        public Task AddAsync(UserSession entity, CancellationToken cancellationToken = default)
        {
            _added.Add(entity);
            return Task.CompletedTask;
        }

        public void Update(UserSession entity)
        {
        }

        public void Remove(UserSession entity)
        {
        }

        public Task<IEnumerable<UserSession>> ListAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IEnumerable<UserSession>>(table.Rows);
        }

        public Task<IEnumerable<UserSession>> ListByUserIdAsync(int userId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(table.Rows.Where(s => s.UserId == userId));
        }

        public Task CompleteAsync(CancellationToken cancellationToken = default)
        {
            foreach (var session in _added)
            {
                Identity.Assign(session, new SessionId(table._nextId++));
                table._rows[session.Id.Value] = Copy(session);
                _read[session] = session.RefreshTokenHash;
            }

            _added.Clear();

            foreach (var (session, hashRead) in _read.ToList())
            {
                var row = table._rows[session.Id.Value];
                if (row.RefreshTokenHash == session.RefreshTokenHash && row.TerminatedAt == session.TerminatedAt)
                    continue; // nothing changed by this request

                // UPDATE ... WHERE id = @id AND refresh_token_hash = @hashRead
                if (row.RefreshTokenHash != hashRead)
                    throw new ConcurrencyConflictException(new InvalidOperationException("0 rows affected"));

                table._rows[session.Id.Value] = Copy(session);
                _read[session] = session.RefreshTokenHash;
            }

            return Task.CompletedTask;
        }

        public Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> work,
            CancellationToken cancellationToken = default)
        {
            return work(cancellationToken);
        }

        private async Task<UserSession?> Lookup(Func<UserSession, bool> predicate)
        {
            var row = table._rows.Values.FirstOrDefault(predicate);
            UserSession? copy = null;
            if (row is not null)
            {
                copy = Copy(row);
                _read[copy] = row.RefreshTokenHash;
            }

            // The other request runs here, after this one has read and before it writes.
            if (_pause is { } pause)
            {
                _pause = null;
                await pause();
            }

            return copy;
        }
    }
}
