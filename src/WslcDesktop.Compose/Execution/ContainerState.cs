using System.Text.Json;

namespace WslcDesktop.Compose.Execution;

internal static class ContainerState
{
    public static bool IsRunning(string state) => state.Equals("running", StringComparison.OrdinalIgnoreCase);
}
