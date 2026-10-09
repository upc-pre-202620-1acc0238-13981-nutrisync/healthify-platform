using Cortex.Mediator;
using Healthify.Platform.Iam.Application.CommandServices;
using Healthify.Platform.Iam.Domain.Model.Aggregates;
using Healthify.Platform.Iam.Domain.Model.Commands;
using Healthify.Platform.Iam.Domain.Model.Errors;
using Healthify.Platform.Iam.Domain.Model.Events;
using Healthify.Platform.Iam.Domain.Model.ValueObjects;
using Healthify.Platform.Iam.Domain.Repositories;
using Healthify.Platform.Iam.Domain.Services;
using Healthify.Platform.Shared.Application.Patterns;
using Healthify.Platform.Shared.Domain.Repositories;

namespace Healthify.Platform.Iam.Application.Internal.CommandServices;

public class UserCommandService(
    IUserRepository userRepository,
    IUnitOfWork unitOfWork,
    IHashingService hashingService,
    ILogger<UserCommandService> logger,
    IMediator mediator) : IUserCommandService
{
    /// <summary>Subflow 1.1 - Account Registration.</summary>
    public async Task<Result<User, IamError>> Handle(RegisterAccountCommand command,
        CancellationToken cancellationToken = default)
    {
        // 1. Value object validation, each one mapped to its own error. The person name first, as the
        //    registration form asks for it first (IAM-1).
        try
        {
            _ = new PersonName(command.GivenNames, command.FamilyNames);
        }
        catch (ArgumentException)
        {
            return new Result<User, IamError>.Failure(IamError.NameRequired);
        }

        Email email;
        try
        {
            email = new Email(command.Email);
        }
        catch (ArgumentException)
        {
            return new Result<User, IamError>.Failure(IamError.InvalidEmail);
        }

        // Business rule: Role Declared At Registration (Subflow 1.1)
        if (string.IsNullOrWhiteSpace(command.Role))
            return new Result<User, IamError>.Failure(IamError.RoleNotDeclared);

        try
        {
            _ = new Role(command.Role);
        }
        catch (ArgumentException)
        {
            return new Result<User, IamError>.Failure(IamError.InvalidRole);
        }

        // Business rule: Strong Password Required (Subflow 1.1), enforced by the value object.
        Password password;
        try
        {
            password = new Password(command.Password);
        }
        catch (ArgumentException)
        {
            return new Result<User, IamError>.Failure(IamError.WeakPassword);
        }

        try
        {
            // 2. Business rule: Unique Email Required (Subflow 1.1). Needs a persistence lookup, so
            //    it lives here; a unique index on the column is the second line of defence.
            if (await userRepository.ExistsByEmailAsync(email, cancellationToken))
                return new Result<User, IamError>.Failure(IamError.EmailAlreadyTaken);

            // 3. Build the aggregate, which re-asserts its own invariants.
            var user = new User(command, hashingService.Hash(password));

            // 4. Persist.
            await userRepository.AddAsync(user, cancellationToken);
            await unitOfWork.CompleteAsync(cancellationToken);

            // 5. Publish, always after the commit.
            await mediator.PublishAsync(
                new AccountCreated(user.Id.Value, user.Email.Value, user.Role.Value, user.FullName),
                cancellationToken);

            return new Result<User, IamError>.Success(user);
        }
        catch (InvalidOperationException)
        {
            return new Result<User, IamError>.Failure(IamError.RoleNotDeclared);
        }
        catch (Exception ex)
        {
            // 6. Safety net. The email is logged, the password never is.
            logger.LogError(ex, "Error registering account for email {Email}", command.Email);
            return new Result<User, IamError>.Failure(IamError.UnexpectedError);
        }
    }

    /// <summary>IAM-3 - Change Preferred Language.</summary>
    public async Task<Result<User, IamError>> Handle(ChangePreferredLanguageCommand command,
        CancellationToken cancellationToken = default)
    {
        // 1. Value object validation.
        PreferredLanguage language;
        try
        {
            language = new PreferredLanguage(command.Language);
        }
        catch (ArgumentException)
        {
            return new Result<User, IamError>.Failure(IamError.InvalidPreferredLanguage);
        }

        try
        {
            // 2. Load the aggregate. The controller already checked the caller is the owner.
            var user = await userRepository.FindByIdAsync(command.UserId, cancellationToken);
            if (user is null) return new Result<User, IamError>.Failure(IamError.UserNotFound);

            // 3. Mutate and persist. No event: nothing outside Iam reacts, and the facade reads it on demand.
            user.ChangePreferredLanguage(language);
            userRepository.Update(user);
            await unitOfWork.CompleteAsync(cancellationToken);

            return new Result<User, IamError>.Success(user);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error changing the preferred language of user {UserId}", command.UserId);
            return new Result<User, IamError>.Failure(IamError.UnexpectedError);
        }
    }
}
