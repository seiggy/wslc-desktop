namespace WslcDesktop.Sessions;

/// <summary>The session wslc.exe uses when no <c>--session</c> is given; the app joins it instead of making its own.</summary>
public static class DefaultSession
{
    public static string Name(string? userName = null) => $"wslc-cli-{userName ?? Environment.UserName}";
}
