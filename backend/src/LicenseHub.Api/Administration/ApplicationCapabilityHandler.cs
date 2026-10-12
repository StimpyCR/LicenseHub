using System.Security.Claims;
using LicenseHub.Application.Administration;
using Microsoft.AspNetCore.Authorization;

namespace LicenseHub.Api.Administration;

public sealed record ApplicationCapabilityRequirement(string Capability) : IAuthorizationRequirement;

// Invoke AuthorizeAsync(user, actualResource.ApplicationId, requirement). Never authorize a resource
// using an unrelated applicationId supplied by the caller. Session capabilities are display metadata.
public sealed class ApplicationCapabilityHandler(IApplicationAccess access)
    : AuthorizationHandler<ApplicationCapabilityRequirement, Guid>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context,
        ApplicationCapabilityRequirement requirement, Guid applicationId)
    {
        if (!context.User.Identities.Any(identity => identity.IsAuthenticated
                && identity.AuthenticationType == Microsoft.AspNetCore.Identity.IdentityConstants.ApplicationScheme)
            || !context.User.HasClaim("licensehub:mfa", "verified")
            || !Guid.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)) return;
        if (await access.IsAllowedAsync(userId, applicationId, requirement.Capability, CancellationToken.None))
            context.Succeed(requirement);
    }
}
