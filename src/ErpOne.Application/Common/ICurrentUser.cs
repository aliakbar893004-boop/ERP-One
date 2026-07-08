namespace ErpOne.Application.Common;

/// <summary>User yang sedang login (nama Windows), untuk stempel audit.</summary>
public interface ICurrentUser
{
    string? UserName { get; }
}

/// <summary>Fallback ketika tidak ada konteks request (mis. design-time / background).</summary>
public sealed class NullCurrentUser : ICurrentUser
{
    public string? UserName => null;
}
