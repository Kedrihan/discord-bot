using Microsoft.AspNetCore.Authorization;

namespace DiscordBot.Core.Permissions;

public class AdminAccessHandler(PermissionService permissionService) : AuthorizationHandler<AdminAccessRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, AdminAccessRequirement requirement)
    {
        if (context.User.Identity?.IsAuthenticated == true && permissionService.HasAnyAdminAccess(context.User))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
