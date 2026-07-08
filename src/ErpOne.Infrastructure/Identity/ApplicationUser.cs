using Microsoft.AspNetCore.Identity;
using ErpOne.Domain.Common;

namespace ErpOne.Infrastructure.Identity;

/// <summary>User aplikasi (AspNetUsers) — IdentityUser + ekstensi DisplayName/IsActive + audit.</summary>
public class ApplicationUser : IdentityUser, IAuditable
{
    public string DisplayName { get; set; } = default!;
    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; private set; }
    public string? CreatedBy { get; private set; }
    public DateTime? ModifiedAt { get; private set; }
    public string? ModifiedBy { get; private set; }

    public void MarkCreated(DateTime at, string? by) { CreatedAt = at; CreatedBy = by; }
    public void MarkModified(DateTime at, string? by) { ModifiedAt = at; ModifiedBy = by; }
}
