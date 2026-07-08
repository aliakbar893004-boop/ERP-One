using ErpOne.Application.Common;

namespace ErpOne.Web.Infrastructure;

/// <summary>Simpan unggahan ke disk lokal di bawah web root; dilayani sebagai file statis.</summary>
public sealed class LocalFileStorage(IWebHostEnvironment env) : IFileStorage
{
    private string WebRoot =>
        env.WebRootPath ?? Path.Combine(env.ContentRootPath, "wwwroot");

    public async Task<StoredFile> SaveAsync(
        Stream content, string originalFileName, string subFolder, CancellationToken ct = default)
    {
        var ext = Path.GetExtension(originalFileName);
        var fileName = $"{Guid.NewGuid():N}{ext}".ToLowerInvariant();

        var relativeDir = subFolder.Replace('\\', '/').Trim('/');
        var absoluteDir = Path.Combine(WebRoot, relativeDir.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(absoluteDir);

        var absolutePath = Path.Combine(absoluteDir, fileName);
        await using (var fs = new FileStream(absolutePath, FileMode.Create, FileAccess.Write))
            await content.CopyToAsync(fs, ct);

        var size = new FileInfo(absolutePath).Length;
        return new StoredFile($"{relativeDir}/{fileName}", size);
    }

    public void Delete(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath)) return;

        var safe = relativePath.Replace('\\', '/').TrimStart('/');
        var absolute = Path.Combine(WebRoot, safe.Replace('/', Path.DirectorySeparatorChar));
        if (File.Exists(absolute))
            File.Delete(absolute);
    }
}
