using System.Collections.Concurrent;

namespace ErpOne.Web.Services;

/// <summary>Menegakkan "satu halaman kasir aktif per user" (in-memory, satu instance server).
/// Kunci diambil saat membuka layar POS dan dilepas saat sirkuit Blazor dibuang.</summary>
public interface IPosSessionRegistry
{
    /// <summary>Klaim slot untuk user. True bila belum ada pemegang lain, atau pemegangnya token yang sama
    /// (re-render/reconnect). False bila sesi lain (token beda) sedang memegang.</summary>
    bool TryAcquire(string userId, string token);

    /// <summary>Lepas slot hanya bila token cocok (dispose sesi lama tak mengusir sesi baru).</summary>
    void Release(string userId, string token);

    /// <summary>Kapan sesi aktif diambil (untuk pesan "dibuka sejak …"), atau null bila bebas.</summary>
    DateTime? ActiveSince(string userId);
}

public sealed class PosSessionRegistry : IPosSessionRegistry
{
    private readonly ConcurrentDictionary<string, (string Token, DateTime Since)> _sessions = new();

    public bool TryAcquire(string userId, string token)
    {
        if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(token)) return false;
        var now = DateTime.Now;
        var current = _sessions.AddOrUpdate(
            userId,
            _ => (token, now),
            (_, existing) => existing); // pemegang berbeda → jangan timpa
        return current.Token == token;
    }

    public void Release(string userId, string token)
    {
        if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(token)) return;
        if (_sessions.TryGetValue(userId, out var e) && e.Token == token)
            _sessions.TryRemove(new KeyValuePair<string, (string Token, DateTime Since)>(userId, e));
    }

    public DateTime? ActiveSince(string userId) =>
        _sessions.TryGetValue(userId, out var e) ? e.Since : null;
}
