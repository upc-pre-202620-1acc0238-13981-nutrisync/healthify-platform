using Healthify.Platform.Iam.Application.Acl;
using Healthify.Platform.Iam.Application.QueryServices;
using Healthify.Platform.Iam.Domain.Model.Aggregates;
using Healthify.Platform.Iam.Domain.Model.Commands;
using Healthify.Platform.Iam.Domain.Model.Queries;
using Healthify.Platform.Iam.Domain.Model.ValueObjects;
using Healthify.Platform.Iam.Infrastructure.Persistence.EFC.Repositories;
using Healthify.Platform.Iam.Interfaces.Acl;
using Healthify.Platform.Tests.TestSupport;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Healthify.Platform.Tests.Iam;

/// <summary>IAM-1. The names cross the boundary in one query for a whole listing, and degrade to nothing.</summary>
[Collection(MySqlCollection.Name)]
public class UsersByIdsFacadeTests
{
    private readonly IUserQueryService _queries = Substitute.For<IUserQueryService>();

    [Fact]
    public async Task Several_accounts_are_read_in_one_query_and_keyed_by_id()
    {
        _queries.Handle(Arg.Any<GetUsersByIdsQuery>(), Arg.Any<CancellationToken>())
            .Returns([NewUser(3, "Ana", "Flores"), NewUser(5, "Luz", "Ramírez")]);

        var users = await new IamContextFacade(_queries).GetUsersByIds([3, 5, 9]);

        await _queries.Received(1).Handle(Arg.Any<GetUsersByIdsQuery>(), Arg.Any<CancellationToken>());
        Assert.Equal(2, users.Count);
        Assert.Equal("Ana Flores", users[3].FullName);
        Assert.Equal('L', users[5].Initial);
        Assert.False(users.ContainsKey(9));
    }

    [Fact]
    public async Task A_failing_lookup_yields_an_empty_dictionary()
    {
        _queries.Handle(Arg.Any<GetUsersByIdsQuery>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("down"));

        Assert.Empty(await new IamContextFacade(_queries).GetUsersByIds([3]));
    }

    [Fact]
    public void An_account_from_before_IAM_1_has_a_full_name_without_trailing_space()
    {
        var item = new UserIdentityItem(1, "juan123@correo.com", "Patient", "juan123");

        Assert.Equal("juan123", item.FullName);
        Assert.Equal('J', item.Initial);
        Assert.Equal('?', new UserIdentityItem(2, "x@y.com", "Patient").Initial);
    }

    [MySqlFact]
    [Trait("Category", "MySql")]
    public async Task The_typed_id_list_is_translated_by_MySql()
    {
        await using var db = await MySqlIntegrationDatabase.CreateAsync(DateOnly.FromDateTime(DateTime.UtcNow));
        int anaId, luzId;
        await using (var context = db.NewContext())
        {
            var ana = new User(new RegisterAccountCommand("ana@correo.com", "x", "Patient", "Ana", "Flores"), "h");
            var luz = new User(new RegisterAccountCommand("luz@correo.com", "x", "Patient", "Luz", "Ramírez"), "h");
            context.Add(ana);
            context.Add(luz);
            await context.SaveChangesAsync();
            anaId = ana.Id.Value;
            luzId = luz.Id.Value;
        }

        await using (var context = db.NewContext())
        {
            var users = await new UserRepository(context).ListByIdsAsync([anaId, luzId, 9999]);

            Assert.Equal(["Ana Flores", "Luz Ramírez"], users.OrderBy(u => u.Id.Value).Select(u => u.FullName));
        }
    }

    private static User NewUser(int id, string given, string family)
    {
        return Identity.Assign(
            new User(new RegisterAccountCommand($"{given}@correo.com", "x", "Patient", given, family), "hash"),
            new UserId(id));
    }
}
