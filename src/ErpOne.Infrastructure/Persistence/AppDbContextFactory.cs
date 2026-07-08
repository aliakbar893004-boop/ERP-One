using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using ErpOne.Application.Common;

namespace ErpOne.Infrastructure.Persistence;

/// <summary>
/// Factory design-time untuk EF Core CLI (migrations). Membuat DbContext TANPA membangun
/// host web — sehingga tooling tidak terpengaruh logging/sink yang butuh koneksi runtime.
/// Connection string dibaca dari appsettings.json startup project (Web) agar migration
/// menyasar database yang sama dengan aplikasi. Tidak dipakai saat aplikasi berjalan.
/// </summary>
public class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        // EF tools menjadikan direktori startup project (Web) sebagai current directory.
        var config = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var connectionString = config.GetConnectionString("Default")
            ?? "Server=localhost;Database=MyApp;Trusted_Connection=True;TrustServerCertificate=True;Encrypt=False";

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(connectionString)
            .Options;

        return new AppDbContext(options, new NullCurrentUser());
    }
}
