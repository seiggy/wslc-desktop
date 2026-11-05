using System.Text.Json;

namespace WslcDesktop.Compose.Execution;

public sealed record StackContainer(string Name, string Service, string State);
