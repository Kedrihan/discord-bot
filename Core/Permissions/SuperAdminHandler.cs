using Microsoft.AspNetCore.Authorization;

namespace DiscordBot.Core.Permissions;

public class SuperAdminHandler(PermissionService permissionService) : AuthorizationHandler<SuperAdminRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, SuperAdminRequirement requirement)
    {
        if (context.User.Identity?.IsAuthenticated == true && permissionService.IsSuperAdmin(context.User))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
