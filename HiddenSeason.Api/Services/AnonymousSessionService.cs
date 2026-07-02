using Microsoft.AspNetCore.DataProtection;

namespace HiddenSeason.Api.Services;

public sealed class AnonymousSessionService
{
    private const string CookieName = "hs_session";
    private readonly IDataProtector _protector;

    public AnonymousSessionService(IDataProtectionProvider provider)
    {
        _protector = provider.CreateProtector("HiddenSeason.AnonymousSession.v1");
    }

    public string GetOrCreate(HttpContext context)
    {
        var protectedValue = context.Request.Cookies[CookieName];
        if (!string.IsNullOrWhiteSpace(protectedValue))
        {
            try
            {
                var sessionId = _protector.Unprotect(protectedValue);
                if (Guid.TryParseExact(sessionId, "N", out _))
                {
                    return sessionId;
                }
            }
            catch
            {
                // Invalid or expired cookies are replaced below.
            }
        }

        var created = Guid.NewGuid().ToString("N");
        context.Response.Cookies.Append(CookieName, _protector.Protect(created), new CookieOptions
        {
            HttpOnly = true,
            Secure = context.Request.IsHttps,
            SameSite = SameSiteMode.Strict,
            Path = "/",
            MaxAge = TimeSpan.FromDays(365),
            IsEssential = true
        });
        return created;
    }
}
