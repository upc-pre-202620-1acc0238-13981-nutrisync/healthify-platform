using Healthify.Platform.Iam.Domain.Model.Commands;
using Healthify.Platform.Iam.Interfaces.REST.Resources;

namespace Healthify.Platform.Iam.Interfaces.REST.Transform;

public static class RegisterAccountCommandAssembler
{
    public static RegisterAccountCommand ToCommand(SignUpResource resource)
    {
        return new RegisterAccountCommand(resource.Email, resource.Password, resource.Role, resource.GivenNames,
            resource.FamilyNames);
    }
}
