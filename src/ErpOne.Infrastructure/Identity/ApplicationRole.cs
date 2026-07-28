using Microsoft.AspNetCore.Identity;
using ErpOne.Domain.Common;

namespace ErpOne.Infrastructure.Identity;

/// <summary>Role aplikasi (AspNetRoles) — IdentityRole + Description + audit. (dulu "User Group")</summary>
public class ApplicationRole : IdentityRole, IAuditable
{
    public string? Description { get; set; }

    /// <summary>Batas diskon manual untuk role ini. null = tidak diatur (pakai default global).
    /// 0 = tidak boleh memberi diskon sama sekali.</summary>
    public decimal? MaxDiscountPercent { get; set; }

    public DateTime CreatedAt { get; private set; }
    public string? CreatedBy { get; private set; }
    public DateTime? ModifiedAt { get; private set; }
    public string? ModifiedBy { get; private set; }

    public ApplicationRole() { }
    public ApplicationRole(string roleName) : base(roleName) { }

    public void MarkCreated(DateTime at, string? by) { CreatedAt = at; CreatedBy = by; }
    public void MarkModified(DateTime at, string? by) { ModifiedAt = at; ModifiedBy = by; }
}
