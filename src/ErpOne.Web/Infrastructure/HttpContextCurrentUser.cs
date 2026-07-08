using ErpOne.Application.Common;

namespace ErpOne.Web.Infrastructure;

/// <summary>Mengambil nama user Windows yang sedang login dari HttpContext.</summary>
public sealed class HttpContextCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    public string? UserName => accessor.HttpContext?.User.Identity?.Name;
}
