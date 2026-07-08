using Microsoft.AspNetCore.Authorization;

namespace ErpOne.Web.Authorization;

public record PermissionRequirement(string Permission) : IAuthorizationRequirement;
