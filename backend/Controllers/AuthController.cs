using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ApplicationAssistant.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    [HttpGet("login")]
    [AllowAnonymous]
    public IActionResult Login([FromQuery] string? returnUrl = null)
    {
        var frontendOrigin = HttpContext.RequestServices
            .GetRequiredService<IConfiguration>()["Frontend:Origin"] ?? "http://localhost:5173";
        var redirect = string.IsNullOrWhiteSpace(returnUrl)
            ? frontendOrigin
            : returnUrl;

        var props = new AuthenticationProperties
        {
            RedirectUri = $"/api/auth/callback?returnUrl={Uri.EscapeDataString(redirect)}"
        };

        return Challenge(props, GoogleDefaults.AuthenticationScheme);
    }

    [HttpGet("callback")]
    [AllowAnonymous]
    public IActionResult Callback([FromQuery] string? returnUrl = null)
    {
        var frontendOrigin = HttpContext.RequestServices
            .GetRequiredService<IConfiguration>()["Frontend:Origin"] ?? "http://localhost:5173";
        var target = string.IsNullOrWhiteSpace(returnUrl) ? frontendOrigin : returnUrl;
        return Redirect(target);
    }

    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Ok(new { ok = true });
    }

    [HttpGet("me")]
    [Authorize]
    public IActionResult Me()
    {
        var userId = User.GetAppUserId();
        return Ok(new
        {
            id = userId,
            email = User.FindFirstValue(ClaimTypes.Email) ?? User.FindFirstValue("email"),
            name = User.FindFirstValue(ClaimTypes.Name) ?? User.FindFirstValue("name")
        });
    }
}
