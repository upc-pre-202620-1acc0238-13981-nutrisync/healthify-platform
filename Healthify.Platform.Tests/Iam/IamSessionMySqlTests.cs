using Cortex.Mediator;
using Healthify.Platform.Iam.Application.Internal;
using Healthify.Platform.Iam.Application.Internal.CommandServices;
using Healthify.Platform.Iam.Domain.Model.Aggregates;
using Healthify.Platform.Iam.Domain.Model.Commands;
using Healthify.Platform.Iam.Domain.Model.Errors;
using Healthify.Platform.Iam.Domain.Model.ValueObjects;
using Healthify.Platform.Iam.Domain.Repositories;
using Healthify.Platform.Iam.Domain.Services;
using Healthify.Platform.Iam.Infrastructure.Persistence.EFC.Repositories;
using Healthify.Platform.Iam.Infrastructure.Tokens.JWT;
using Healthify.Platform.Iam.Infrastructure.Tokens.Refresh;
using Healthify.Platform.Shared.Application.Patterns;
using Healthify.Platform.Shared.Infrastructure.Persistence.EFC.Repositories;
using Healthify.Platform.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Healthify.Platform.Tests.Iam;

/// <summary>
///     IAM-3 and IAM-4 against a real MySQL (Category=MySql; skipped without HEALTHIFY_IT_MYSQL): the language
///     column and its default, and the refresh token columns through sign-in, rotation, reuse and sign-out, each
///     step in its own context as each request would be.
/// </summary>
[Collection(MySqlCollection.Name)]
[Trait("Category", "MySql")]
public class IamSessionMySqlTests
{
    private const string StrongPassword = "Secreta#2026";

    private static readonly IConfiguration Configuration = new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
            { ["TokenSettings:Secret"] = "a-test-secret-that-is-long-enough-for-hmac-256" })
        .Build();

    [MySqlFact]
    public async Task Accounts_default_to_spanish_and_keep_the_language_they_choose()
    {
        await using var db = await MySqlIntegrationDatabase.CreateAsync(DateOnly.FromDateTime(DateTime.UtcNow));
        await using (var context = db.NewContext())
        {
            // A row written without the column, as every account before IAM-3 was.
            await context.Database.ExecuteSqlRawAsync(
                "INSERT INTO users (email, given_names, family_names, password_hash, role, failed_sign_in_attempts) " +
                "VALUES ('legacy@correo.com', 'legacy', '', 'hash', 'Patient', 0)");
            context.Add(NewUser("ana@correo.com"));
            await context.SaveChangesAsync();
        }

        await using (var context = db.NewContext())
        {
            var users = new UserRepository(context);
            var legacy = await users.FindByEmailAsync(new Email("legacy@correo.com"));
            Assert.Equal("es", legacy!.PreferredLanguage.Value);

            var ana = await users.FindByEmailAsync(new Email("ana@correo.com"));
            ana!.ChangePreferredLanguage(new PreferredLanguage("en"));
            await context.SaveChangesAsync();
        }

        await using (var context = db.NewContext())
        {
            var ana = await new UserRepository(context).FindByEmailAsync(new Email("ana@correo.com"));
            Assert.Equal("en", ana!.PreferredLanguage.Value);
        }
    }

    [MySqlFact]
    public async Task A_refresh_token_rotates_across_requests_and_its_reuse_ends_the_session()
    {
        await using var db = await MySqlIntegrationDatabase.CreateAsync(DateOnly.FromDateTime(DateTime.UtcNow));
        await SeedUser(db);

        var signIn = Success(await Request(db, s => s.Handle(new SignInCommand("ana@correo.com", StrongPassword))));
        var rotated = Success(await Request(db, s => s.Handle(new RefreshSessionCommand(signIn.RefreshToken))));

        // A retry within the grace window, read back from the database: the same pair, the session lives.
        var retry = Success(await Request(db, s => s.Handle(new RefreshSessionCommand(signIn.RefreshToken))));
        Assert.Equal((rotated.Token, rotated.RefreshToken, rotated.ExpiresAt),
            (retry.Token, retry.RefreshToken, retry.ExpiresAt));
        Assert.True((await Session(db, signIn.Session.Id.Value)).IsActive);

        var again = Success(await Request(db, s => s.Handle(new RefreshSessionCommand(rotated.RefreshToken))));

        // The token from sign-in, once its successor was used, is reuse: the session ends with every token.
        Assert.Equal(IamError.RefreshTokenInvalid,
            Failure(await Request(db, s => s.Handle(new RefreshSessionCommand(signIn.RefreshToken)))));
        var session = await Session(db, signIn.Session.Id.Value);
        Assert.False(session.IsActive);
        Assert.Null(session.RefreshTokenHash);
        Assert.Equal(IamError.RefreshTokenInvalid,
            Failure(await Request(db, s => s.Handle(new RefreshSessionCommand(again.RefreshToken)))));
    }

    [MySqlFact]
    public async Task A_refresh_that_loses_the_update_replays_the_winner()
    {
        await using var db = await MySqlIntegrationDatabase.CreateAsync(DateOnly.FromDateTime(DateTime.UtcNow));
        await SeedUser(db);
        var signIn = Success(await Request(db, s => s.Handle(new SignInCommand("ana@correo.com", StrongPassword))));
        Result<SignInOutcome, IamError>? winner = null;

        // B reads the row; A rotates and commits; B's UPDATE ... WHERE refresh_token_hash = <read> hits 0 rows.
        var loser = await Request(db, s => s.Handle(new RefreshSessionCommand(signIn.RefreshToken)),
            async () => winner = await Request(db, s => s.Handle(new RefreshSessionCommand(signIn.RefreshToken))));

        var a = Success(winner!);
        var b = Success(loser);
        Assert.Equal((a.Token, a.RefreshToken, a.ExpiresAt), (b.Token, b.RefreshToken, b.ExpiresAt));
        Assert.True((await Session(db, signIn.Session.Id.Value)).IsActive);
        Assert.True((await Request(db, s => s.Handle(new RefreshSessionCommand(a.RefreshToken)))).IsSuccess);
    }

    [MySqlFact]
    public async Task Two_simultaneous_refreshes_both_get_the_same_valid_pair()
    {
        await using var db = await MySqlIntegrationDatabase.CreateAsync(DateOnly.FromDateTime(DateTime.UtcNow));
        await SeedUser(db);
        var signIn = Success(await Request(db, s => s.Handle(new SignInCommand("ana@correo.com", StrongPassword))));

        var results = await Task.WhenAll(
            Task.Run(() => Request(db, s => s.Handle(new RefreshSessionCommand(signIn.RefreshToken)))),
            Task.Run(() => Request(db, s => s.Handle(new RefreshSessionCommand(signIn.RefreshToken)))));

        var a = Success(results[0]);
        var b = Success(results[1]);
        Assert.Equal((a.Token, a.RefreshToken, a.ExpiresAt), (b.Token, b.RefreshToken, b.ExpiresAt));
        Assert.True((await Session(db, signIn.Session.Id.Value)).IsActive);
        Assert.True((await Request(db, s => s.Handle(new RefreshSessionCommand(a.RefreshToken)))).IsSuccess);
    }

    private static async Task SeedUser(MySqlIntegrationDatabase db)
    {
        await using var context = db.NewContext();
        context.Add(NewUser("ana@correo.com"));
        await context.SaveChangesAsync();
    }

    [MySqlFact]
    public async Task The_expiry_survives_the_round_trip_and_sign_out_clears_the_columns()
    {
        await using var db = await MySqlIntegrationDatabase.CreateAsync(DateOnly.FromDateTime(DateTime.UtcNow));
        await using (var context = db.NewContext())
        {
            context.Add(NewUser("ana@correo.com"));
            await context.SaveChangesAsync();
        }

        var signIn = Success(await Request(db, s => s.Handle(new SignInCommand("ana@correo.com", StrongPassword))));
        var stored = await Session(db, signIn.Session.Id.Value);
        Assert.Matches("^[0-9a-f]{64}$", stored.RefreshTokenHash);
        Assert.InRange(stored.RefreshTokenExpiresAt!.Value, DateTimeOffset.UtcNow.AddDays(29.9),
            DateTimeOffset.UtcNow.AddDays(30.1));
        Assert.True(stored.AcceptsRefreshToken(stored.RefreshTokenHash!, DateTimeOffset.UtcNow));
        Assert.False(stored.AcceptsRefreshToken(stored.RefreshTokenHash!, DateTimeOffset.UtcNow.AddDays(31)));

        Assert.True((await Request(db, s => s.Handle(new SignOutCommand(signIn.Session.Id.Value, signIn.User.Id.Value))))
            .IsSuccess);

        var signedOut = await Session(db, signIn.Session.Id.Value);
        Assert.Equal((null, null, null), (signedOut.RefreshTokenHash, signedOut.RefreshTokenExpiresAt,
            signedOut.PreviousRefreshTokenHash));
    }

    private static User NewUser(string email)
    {
        return new User(new RegisterAccountCommand(email, StrongPassword, "Patient", "Ana", "Flores"), "hash");
    }

    private static async Task<T> Request<T>(MySqlIntegrationDatabase db,
        Func<UserSessionCommandService, Task<T>> call, Func<Task>? afterFirstLookup = null)
    {
        await using var context = db.NewContext();
        var hashing = Substitute.For<IHashingService>();
        hashing.Verify(StrongPassword, "hash").Returns(true);
        IUserSessionRepository sessions = new UserSessionRepository(context);
        if (afterFirstLookup is not null) sessions = new PausesAfterFirstLookup(sessions, afterFirstLookup);
        var service = new UserSessionCommandService(new UserRepository(context), sessions,
            new UnitOfWork(context), hashing, new JwtTokenService(Configuration),
            new RefreshTokenService(Configuration, TimeProvider.System),
            NullLogger<UserSessionCommandService>.Instance, Substitute.For<IMediator>(),
            new Healthify.Platform.Iam.Infrastructure.Security.ConfiguredSignInLockoutPolicy(
                new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build()));
        return await call(service);
    }

    private static async Task<UserSession> Session(MySqlIntegrationDatabase db, int sessionId)
    {
        await using var context = db.NewContext();
        return (await new UserSessionRepository(context).FindByIdAsync(sessionId))!;
    }

    private static SignInOutcome Success(Result<SignInOutcome, IamError> result)
    {
        return Assert.IsType<Result<SignInOutcome, IamError>.Success>(result).Value;
    }

    private static IamError Failure(Result<SignInOutcome, IamError> result)
    {
        return Assert.IsType<Result<SignInOutcome, IamError>.Failure>(result).Error;
    }

    /// <summary>The real repository, except that another request runs right after its first lookup.</summary>
    private sealed class PausesAfterFirstLookup(IUserSessionRepository inner, Func<Task> pause)
        : IUserSessionRepository
    {
        private Func<Task>? _pause = pause;

        public async Task<UserSession?> FindByRefreshTokenHashAsync(string tokenHash,
            CancellationToken cancellationToken = default)
        {
            var session = await inner.FindByRefreshTokenHashAsync(tokenHash, cancellationToken);
            if (_pause is { } run)
            {
                _pause = null;
                await run();
            }

            return session;
        }

        public Task<UserSession?> FindByPreviousRefreshTokenHashAsync(string tokenHash,
            CancellationToken cancellationToken = default)
        {
            return inner.FindByPreviousRefreshTokenHashAsync(tokenHash, cancellationToken);
        }

        public Task ReloadAsync(UserSession session, CancellationToken cancellationToken = default)
        {
            return inner.ReloadAsync(session, cancellationToken);
        }

        public Task<IEnumerable<UserSession>> ListByUserIdAsync(int userId, CancellationToken cancellationToken = default)
        {
            return inner.ListByUserIdAsync(userId, cancellationToken);
        }

        public Task AddAsync(UserSession entity, CancellationToken cancellationToken = default)
        {
            return inner.AddAsync(entity, cancellationToken);
        }

        public Task<UserSession?> FindByIdAsync(int id, CancellationToken cancellationToken = default)
        {
            return inner.FindByIdAsync(id, cancellationToken);
        }

        public void Update(UserSession entity)
        {
            inner.Update(entity);
        }

        public void Remove(UserSession entity)
        {
            inner.Remove(entity);
        }

        public Task<IEnumerable<UserSession>> ListAsync(CancellationToken cancellationToken = default)
        {
            return inner.ListAsync(cancellationToken);
        }
    }
}
