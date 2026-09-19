using System.Security.Claims;

namespace ApplicationAssistant.Api;

public static class ClaimsPrincipalExtensions
{
    public static Guid? GetAppUserId(this ClaimsPrincipal user)
    {
        var value = user.FindFirstValue("app_user_id");
        return Guid.TryParse(value, out var id) ? id : null;
    }
}
