using System.Text.Json;

namespace WslcDesktop.Containers.Inspection;

public sealed record NetworkInfo(string Name, string IpAddress, string Gateway, string Mac);
