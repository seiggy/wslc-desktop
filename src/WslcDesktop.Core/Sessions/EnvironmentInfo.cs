using System.Diagnostics;
using System.Text.Json;

namespace WslcDesktop.Sessions;

public sealed record EnvironmentInfo(string Wsl, string Kernel, string SessionManager, IReadOnlyList<SessionInfo> Sessions);
