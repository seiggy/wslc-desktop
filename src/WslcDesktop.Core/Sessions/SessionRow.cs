using System.Diagnostics;
using System.Text.Json;

namespace WslcDesktop.Sessions;

public sealed record SessionRow(string Id, string CreatorPid, string Name);
