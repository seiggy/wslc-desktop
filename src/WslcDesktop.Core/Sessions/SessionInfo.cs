using System.Diagnostics;
using System.Text.Json;

namespace WslcDesktop.Sessions;

public sealed record SessionInfo(
    string Id, string Name, string Creator, string CreatorPid, string Containers, string Images, bool IsThisApp)
{
    public bool CanTerminate => !IsThisApp;

    public string CreatorText => $"{Creator} ({CreatorPid})";
}
