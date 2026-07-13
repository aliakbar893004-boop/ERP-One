using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using ErpOne.Application.Common;
using ErpOne.Infrastructure;
using ErpOne.Infrastructure.Identity;
using ErpOne.Infrastructure.Persistence;
using ErpOne.Web.Account;
using ErpOne.Web.Authorization;
using ErpOne.Web.Components;
using ErpOne.Web.Endpoints;
using ErpOne.Web.Infrastructure;
using Scalar.AspNetCore;
using Serilog;
using Serilog.Events;
using Serilog.Sinks.MSSqlServer;

var builder = WebApplication.CreateBuilder(args);

// QuestPDF Community license (free for orgs with < USD 1M annual revenue).
QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

// Logging. Console + File dari appsettings; SQL sink hanya di luar "Testing".
builder.Host.UseSerilog((context, config) =>
{
    config.ReadFrom.Configuration(context.Configuration);
    if (!context.HostingEnvironment.IsEnvironment("Testing"))
    {
        config.WriteTo.MSSqlServer(
            connectionString: context.Configuration.GetConnectionString("Default"),
            sinkOptions: new MSSqlServerSinkOptions { TableName = "Logs", SchemaName = "dbo", AutoCreateSqlTable = true },
            restrictedToMinimumLevel: LogEventLevel.Warning);
    }
});

// Infrastructure (EF Core + service + validators)
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, HttpContextCurrentUser>();

// Penyimpanan file lokal (gambar produk) di bawah wwwroot.
builder.Services.AddScoped<IFileStorage, LocalFileStorage>();

// SweetAlert2 interop (dialog & toast)
builder.Services.AddScoped<SwalService>();

// Kunci "satu halaman kasir aktif per user" (in-memory, satu instance server)
builder.Services.AddSingleton<ErpOne.Web.Services.IPosSessionRegistry, ErpOne.Web.Services.PosSessionRegistry>();

// Permission-based authorization infrastructure
builder.Services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();
builder.Services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();

// ASP.NET Core Identity + cookie authentication
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<AuthenticationStateProvider, IdentityRevalidatingAuthenticationStateProvider>();

builder.Services.AddAuthentication(IdentityConstants.ApplicationScheme)
    .AddIdentityCookies();

builder.Services.AddIdentityCore<ApplicationUser>(options =>
    {
        options.SignIn.RequireConfirmedAccount = false;
        options.User.RequireUniqueEmail = false;
        options.Password.RequiredLength = 6;
    })
    .AddRoles<ApplicationRole>()
    .AddEntityFrameworkStores<AppDbContext>()
    .AddSignInManager()
    .AddDefaultTokenProviders();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Account/Login";
    options.AccessDeniedPath = "/Account/AccessDenied";
});

var managerRole = builder.Configuration["Identity:ManagerRole"] ?? "Administrators";
builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();

    // Legacy: dipakai ProductEndpoints (REST API)
    options.AddPolicy(AppPolicies.ManageProducts, policy => policy.RequireRole(managerRole));

    // Tampilkan grup Master jika memiliki setidaknya satu izin master.*.index
    options.AddPolicy("master.any", policy => policy
        .RequireAuthenticatedUser()
        .RequireAssertion(ctx =>
            AppMenus.AllResources
                .Where(r => r.Key.StartsWith("master."))
                .Any(r => ctx.User.HasClaim(AppMenus.ClaimType, $"{r.Key}.index"))));

    // Tampilkan grup Settings jika memiliki setidaknya satu izin settings.*.index
    options.AddPolicy("settings.any", policy => policy
        .RequireAuthenticatedUser()
        .RequireAssertion(ctx =>
            AppMenus.AllResources
                .Where(r => r.Key.StartsWith("settings."))
                .Any(r => ctx.User.HasClaim(AppMenus.ClaimType, $"{r.Key}.index"))));

    // Tampilkan grup Inventory jika memiliki setidaknya satu izin inventory.*.index
    options.AddPolicy("inventory.any", policy => policy
        .RequireAuthenticatedUser()
        .RequireAssertion(ctx =>
            AppMenus.AllResources
                .Where(r => r.Key.StartsWith("inventory."))
                .Any(r => ctx.User.HasClaim(AppMenus.ClaimType, $"{r.Key}.index"))));

    // Tampilkan grup Transaksi jika memiliki setidaknya satu izin transactions.*.index
    options.AddPolicy("transactions.any", policy => policy
        .RequireAuthenticatedUser()
        .RequireAssertion(ctx =>
            AppMenus.AllResources
                .Where(r => r.Key.StartsWith("transactions."))
                .Any(r => ctx.User.HasClaim(AppMenus.ClaimType, $"{r.Key}.index"))));

    // Tampilkan grup Kasir jika memiliki setidaknya satu izin cashier.*.index
    options.AddPolicy("cashier.any", policy => policy
        .RequireAuthenticatedUser()
        .RequireAssertion(ctx =>
            AppMenus.AllResources
                .Where(r => r.Key.StartsWith("cashier."))
                .Any(r => ctx.User.HasClaim(AppMenus.ClaimType, $"{r.Key}.index"))));
});

// Blazor (Interactive Server)
builder.Services.AddRazorComponents().AddInteractiveServerComponents();

// REST API surface
builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ValidationExceptionHandler>();

var app = builder.Build();

app.UseSerilogRequestLogging();
app.UseExceptionHandler();

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}
else
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

// Halaman status hanya untuk UI; API kembalikan status code apa adanya.
app.UseWhen(
    context => !context.Request.Path.StartsWithSegments("/api"),
    branch => branch.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true));

app.UseHttpsRedirection();

// Layani file unggahan runtime (mis. /uploads/products/*) dari wwwroot.
app.UseStaticFiles();

app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapStaticAssets().AllowAnonymous();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapProductEndpoints();
app.MapProductImportEndpoints(); // template Excel impor produk
app.MapAccountEndpoints(); // logout

// Bootstrap admin awal (lewati di Testing).
if (!app.Environment.IsEnvironment("Testing"))
    await app.SeedBootstrapAdminsAsync();

app.Run();

public partial class Program { } // agar diakses integration tests
