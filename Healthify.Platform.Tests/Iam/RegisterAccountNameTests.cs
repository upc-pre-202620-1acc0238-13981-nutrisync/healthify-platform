using Cortex.Mediator;
using Healthify.Platform.Iam.Application.Internal.CommandServices;
using Healthify.Platform.Iam.Domain.Model.Aggregates;
using Healthify.Platform.Iam.Domain.Model.Commands;
using Healthify.Platform.Iam.Domain.Model.Errors;
using Healthify.Platform.Iam.Domain.Model.Events;
using Healthify.Platform.Iam.Domain.Model.ValueObjects;
using Healthify.Platform.Iam.Domain.Repositories;
using Healthify.Platform.Iam.Domain.Services;
using Healthify.Platform.Shared.Application.Patterns;
using Healthify.Platform.Shared.Domain.Repositories;
using Healthify.Platform.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Healthify.Platform.Tests.Iam;

/// <summary>
///     IAM-1. Registration requires given names and family names. The name is the first value object
///     validated, and a missing part fails before anything is read or written.
/// </summary>
public class RegisterAccountNameTests
{
    private const string StrongPassword = "Secreta#2026";

    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly IHashingService _hashing = Substitute.For<IHashingService>();
    private readonly IMediator _mediator = Substitute.For<IMediator>();

    public RegisterAccountNameTests()
    {
        _hashing.Hash(Arg.Any<Password>()).Returns("hash");
        _users.AddAsync(Arg.Do<User>(u => Identity.Assign(u, new UserId(7))), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
    }

    [Theory]
    [InlineData(null, "Flores")]
    [InlineData("Ana", null)]
    [InlineData("Ana", "")]
    [InlineData("Ana3", "Flores")]
    public async Task A_missing_or_invalid_name_is_NameRequired_and_nothing_is_persisted(string? given,
        string? family)
    {
        var result = await Service().Handle(
            new RegisterAccountCommand("ana@correo.com", StrongPassword, "Patient", given, family));

        var failure = Assert.IsType<Result<User, IamError>.Failure>(result);
        Assert.Equal(IamError.NameRequired, failure.Error);
        await _users.DidNotReceiveWithAnyArgs().ExistsByEmailAsync(default!, default);
        await _unitOfWork.DidNotReceiveWithAnyArgs().CompleteAsync(default);
        Assert.Empty(Fakes.Published(_mediator));
    }

    [Fact]
    public async Task The_name_is_validated_before_the_email()
    {
        var result = await Service().Handle(new RegisterAccountCommand("not-an-email", StrongPassword, "Patient"));

        Assert.Equal(IamError.NameRequired, Assert.IsType<Result<User, IamError>.Failure>(result).Error);
    }

    [Fact]
    public async Task The_account_keeps_the_name_and_AccountCreated_carries_the_full_name()
    {
        var result = await Service().Handle(
            new RegisterAccountCommand("ana@correo.com", StrongPassword, "Patient", " Ana  María ", "Flores"));

        var user = Assert.IsType<Result<User, IamError>.Success>(result).Value;
        Assert.Equal("Ana María", user.GivenNames);
        Assert.Equal("Flores", user.FamilyNames);
        Assert.Equal("Ana María Flores", user.FullName);

        var created = Assert.IsType<AccountCreated>(Assert.Single(Fakes.Published(_mediator)));
        Assert.Equal("Ana María Flores", created.FullName);
    }

    private UserCommandService Service()
    {
        return new UserCommandService(_users, _unitOfWork, _hashing, NullLogger<UserCommandService>.Instance,
            _mediator);
    }
}
