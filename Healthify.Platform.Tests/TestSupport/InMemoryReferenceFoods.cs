using Healthify.Platform.FoodCatalog.Domain.Model.Aggregates;
using Healthify.Platform.FoodCatalog.Domain.Model.ValueObjects;
using Healthify.Platform.FoodCatalog.Domain.Repositories;
using Healthify.Platform.Shared.Domain.Repositories;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Healthify.Platform.Tests.TestSupport;

/// <summary>
///     IN-7. The food catalog in memory, shared by several "requests" (each with its own pending changes, as each
///     request has its own DbContext), with the unique index on <c>source_hash</c> enforced at commit, as MySQL does.
///     Reads see only what was committed. <see cref="BeforeCommit" /> lets a test hold commits to force a race.
/// </summary>
public sealed class InMemoryReferenceFoods
{
    private readonly object _gate = new();
    private readonly List<ReferenceFood> _stored = [];
    private int _nextId = 1;

    public IReadOnlyList<ReferenceFood> Stored
    {
        get
        {
            lock (_gate) return _stored.ToList();
        }
    }

    /// <summary>Runs before each commit (outside the lock), for instance to wait at a barrier.</summary>
    public Func<Task>? BeforeCommit { get; set; }

    public ReferenceFood Seed(ReferenceFood food)
    {
        lock (_gate)
        {
            Identity.Assign(food, new ReferenceFoodId(_nextId++));
            _stored.Add(food);
            return food;
        }
    }

    /// <summary>A request: its repository and its unit of work share the pending changes.</summary>
    public Request NewRequest()
    {
        return new Request(this);
    }

    public sealed class Request(InMemoryReferenceFoods store) : IReferenceFoodRepository, IUnitOfWork
    {
        private readonly List<ReferenceFood> _pending = [];

        public int Removed { get; private set; }

        public Task AddAsync(ReferenceFood entity, CancellationToken cancellationToken = default)
        {
            _pending.Add(entity);
            return Task.CompletedTask;
        }

        public Task<ReferenceFood?> FindByIdAsync(int id, CancellationToken cancellationToken = default)
        {
            lock (store._gate) return Task.FromResult(store._stored.FirstOrDefault(f => f.Id.Value == id));
        }

        public void Update(ReferenceFood entity)
        {
        }

        /// <summary>As EF: removing an entity that was only added detaches it.</summary>
        public void Remove(ReferenceFood entity)
        {
            if (_pending.Remove(entity)) Removed++;
        }

        public Task<IEnumerable<ReferenceFood>> ListAsync(CancellationToken cancellationToken = default)
        {
            lock (store._gate) return Task.FromResult<IEnumerable<ReferenceFood>>(store._stored.ToList());
        }

        public Task<ReferenceFood?> FindBySourceHashAsync(SourceHash sourceHash,
            CancellationToken cancellationToken = default)
        {
            lock (store._gate) return Task.FromResult(store._stored.FirstOrDefault(f => f.SourceHash == sourceHash));
        }

        public Task<IEnumerable<ReferenceFood>> SearchByLocalNameAsync(string term, int max,
            CancellationToken cancellationToken = default)
        {
            lock (store._gate)
                return Task.FromResult<IEnumerable<ReferenceFood>>(store._stored
                    .Where(f => string.IsNullOrWhiteSpace(term) ||
                                f.LocalNameText.Contains(term.Trim(), StringComparison.OrdinalIgnoreCase))
                    .Take(max).ToList());
        }

        public Task<IEnumerable<ReferenceFood>> ListLocalCatalogAsync(int max,
            CancellationToken cancellationToken = default)
        {
            return SearchByLocalNameAsync(string.Empty, max, cancellationToken);
        }

        public Task<bool> ExistsLocalOverrideWithNameAsync(string localName,
            CancellationToken cancellationToken = default)
        {
            lock (store._gate)
                return Task.FromResult(store._stored.Any(f => f.IsLocalOverride && f.LocalNameText == localName.Trim()));
        }

        public Task<int> CountAsync(CancellationToken cancellationToken = default)
        {
            lock (store._gate) return Task.FromResult(store._stored.Count);
        }

        public Task<IReadOnlyList<ReferenceFood>> ListVerifiedAsync(int max, IReadOnlyCollection<int> excludedIds,
            CancellationToken cancellationToken = default)
        {
            lock (store._gate)
                return Task.FromResult<IReadOnlyList<ReferenceFood>>(store._stored
                    .Where(f => f.IsVerified && !excludedIds.Contains(f.Id.Value))
                    .OrderByDescending(f => f.IsLocalOverride).ThenBy(f => f.LocalNameText, StringComparer.Ordinal)
                    .Take(max).ToList());
        }

        public Task<IReadOnlyList<ReferenceFood>> FindByIdsAsync(IReadOnlyCollection<int> ids,
            CancellationToken cancellationToken = default)
        {
            lock (store._gate)
                return Task.FromResult<IReadOnlyList<ReferenceFood>>(store._stored.Where(f => ids.Contains(f.Id.Value))
                    .ToList());
        }

        public async Task CompleteAsync(CancellationToken cancellationToken = default)
        {
            if (store.BeforeCommit is { } before) await before();

            lock (store._gate)
            {
                // The unique index ix_reference_foods_source_hash.
                if (_pending.Any(p => store._stored.Any(s => s.SourceHash == p.SourceHash)))
                    throw new InvalidOperationException("Duplicate entry for key 'ix_reference_foods_source_hash'.");

                foreach (var food in _pending)
                {
                    Identity.Assign(food, new ReferenceFoodId(store._nextId++));
                    store._stored.Add(food);
                }

                _pending.Clear();
            }
        }

        public Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> work,
            CancellationToken cancellationToken = default)
        {
            return work(cancellationToken);
        }
    }
}

/// <summary>A logger that keeps every formatted message and exception text, to prove what is never logged.</summary>
public sealed class CapturingLogger<T> : ILogger<T>
{
    private readonly List<string> _lines = [];

    public IReadOnlyList<string> Lines
    {
        get
        {
            lock (_lines) return _lines.ToList();
        }
    }

    public IDisposable BeginScope<TState>(TState state) where TState : notnull
    {
        return NullLogger.Instance.BeginScope(state)!;
    }

    public bool IsEnabled(LogLevel logLevel)
    {
        return true;
    }

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        var line = formatter(state, exception) + " " + state + " " + exception;
        lock (_lines) _lines.Add(line);
    }
}
