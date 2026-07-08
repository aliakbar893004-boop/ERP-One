using ErpOne.Web.Services;
using Xunit;

namespace ErpOne.IntegrationTests;

public class PosSessionRegistryTests
{
    [Fact]
    public void First_acquire_wins_second_token_is_blocked()
    {
        var reg = new PosSessionRegistry();
        Assert.True(reg.TryAcquire("u1", "tokenA"));
        Assert.False(reg.TryAcquire("u1", "tokenB")); // sesi lain diblokir
        Assert.True(reg.TryAcquire("u1", "tokenA"));  // sesi sama boleh re-acquire (reconnect)
    }

    [Fact]
    public void Release_with_wrong_token_does_not_free_the_slot()
    {
        var reg = new PosSessionRegistry();
        reg.TryAcquire("u1", "tokenA");
        reg.Release("u1", "tokenB");                   // token salah → tidak melepas
        Assert.False(reg.TryAcquire("u1", "tokenB"));
    }

    [Fact]
    public void Release_with_correct_token_frees_the_slot()
    {
        var reg = new PosSessionRegistry();
        reg.TryAcquire("u1", "tokenA");
        reg.Release("u1", "tokenA");
        Assert.True(reg.TryAcquire("u1", "tokenB"));   // slot bebas → user lain/tab lain boleh
    }

    [Fact]
    public void Different_users_are_independent()
    {
        var reg = new PosSessionRegistry();
        Assert.True(reg.TryAcquire("u1", "a"));
        Assert.True(reg.TryAcquire("u2", "b"));
    }

    [Fact]
    public void ActiveSince_reports_holder_and_null_when_free()
    {
        var reg = new PosSessionRegistry();
        Assert.Null(reg.ActiveSince("u1"));
        reg.TryAcquire("u1", "a");
        Assert.NotNull(reg.ActiveSince("u1"));
    }
}
