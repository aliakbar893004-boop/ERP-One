using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using ErpOne.Domain.Entities;
using ErpOne.Infrastructure.Identity;
using ErpOne.Infrastructure.Persistence;
using ErpOne.Web.Authorization;
// AccountingSeeder lives in ErpOne.Infrastructure.Persistence (already imported above).

namespace ErpOne.Web.Infrastructure;

public static class BootstrapSeeder
{
    public static async Task SeedBootstrapAdminsAsync(this WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var sp = scope.ServiceProvider;

        var db = sp.GetRequiredService<AppDbContext>();
        if (!await db.Database.CanConnectAsync())
            return;

        var config   = app.Configuration;
        var roleName = config["Identity:ManagerRole"] ?? "Administrators";
        var userName = config["Bootstrap:AdminUserName"];
        var password = config["Bootstrap:AdminPassword"];
        if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrWhiteSpace(password))
            return;

        var roleManager = sp.GetRequiredService<RoleManager<ApplicationRole>>();
        var userManager = sp.GetRequiredService<UserManager<ApplicationUser>>();

        // Buat role jika belum ada
        if (!await roleManager.RoleExistsAsync(roleName))
            await roleManager.CreateAsync(new ApplicationRole(roleName) { Description = "Administrators" });

        // Berikan SEMUA permission ke role admin (idempotent)
        var role = (await roleManager.FindByNameAsync(roleName))!;
        var existing = (await roleManager.GetClaimsAsync(role))
            .Where(c => c.Type == AppMenus.ClaimType)
            .Select(c => c.Value)
            .ToHashSet();

        foreach (var perm in AppMenus.AllPermissions.Except(existing))
            await roleManager.AddClaimAsync(role, new Claim(AppMenus.ClaimType, perm));

        // Seed rantai approval default untuk Purchase Order (idempotent).
        // Default memakai role admin agar role pasti ada; admin sebaiknya mengkonfigurasi
        // rantai sebenarnya (role approver non-admin) di Settings → Approval Chain.
        if (!await db.ApprovalChainSteps.AnyAsync(c => c.DocumentType == ApprovalDocumentType.PurchaseOrder))
        {
            db.ApprovalChainSteps.Add(new ApprovalChainStep(ApprovalDocumentType.PurchaseOrder, 1, roleName));
            await db.SaveChangesAsync();
        }

        // Seed rantai approval default untuk Sales Order (idempotent), mengikuti pola PO.
        if (!await db.ApprovalChainSteps.AnyAsync(c => c.DocumentType == ApprovalDocumentType.SalesOrder))
        {
            db.ApprovalChainSteps.Add(new ApprovalChainStep(ApprovalDocumentType.SalesOrder, 1, roleName));
            await db.SaveChangesAsync();
        }

        // Seed rantai approval default untuk Supplier Payment (idempotent), mengikuti pola PO/SO.
        if (!await db.ApprovalChainSteps.AnyAsync(c => c.DocumentType == ApprovalDocumentType.SupplierPayment))
        {
            db.ApprovalChainSteps.Add(new ApprovalChainStep(ApprovalDocumentType.SupplierPayment, 1, roleName));
            await db.SaveChangesAsync();
        }

        // Seed rantai approval default untuk Stock Transfer (idempotent), mengikuti pola PO/SO.
        if (!await db.ApprovalChainSteps.AnyAsync(c => c.DocumentType == ApprovalDocumentType.StockTransfer))
        {
            db.ApprovalChainSteps.Add(new ApprovalChainStep(ApprovalDocumentType.StockTransfer, 1, roleName));
            await db.SaveChangesAsync();
        }

        // Seed rantai approval default untuk Stock Opname (idempotent), mengikuti pola Stock Transfer.
        if (!await db.ApprovalChainSteps.AnyAsync(c => c.DocumentType == ApprovalDocumentType.StockOpname))
        {
            db.ApprovalChainSteps.Add(new ApprovalChainStep(ApprovalDocumentType.StockOpname, 1, roleName));
            await db.SaveChangesAsync();
        }

        // Seed rantai approval default untuk POS Void (idempotent).
        if (!await db.ApprovalChainSteps.AnyAsync(c => c.DocumentType == ApprovalDocumentType.PosSaleVoid))
        {
            db.ApprovalChainSteps.Add(new ApprovalChainStep(ApprovalDocumentType.PosSaleVoid, 1, roleName));
            await db.SaveChangesAsync();
        }

        // Seed COA + posting configuration + master GL accounts (idempotent).
        await AccountingSeeder.SeedAsync(db);

        // Buat user admin jika belum ada
        var user = await userManager.FindByNameAsync(userName);
        if (user is null)
        {
            user = new ApplicationUser
            {
                UserName       = userName,
                Email          = config["Bootstrap:AdminEmail"],
                EmailConfirmed = true,
                DisplayName    = config["Bootstrap:AdminDisplayName"] ?? userName,
                IsActive       = true
            };
            await userManager.CreateAsync(user, password);
        }

        if (!await userManager.IsInRoleAsync(user, roleName))
            await userManager.AddToRoleAsync(user, roleName);
    }
}
