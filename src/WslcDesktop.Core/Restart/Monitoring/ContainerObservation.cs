using System.Globalization;
using System.Text.RegularExpressions;

namespace WslcDesktop.Restart.Monitoring;

public sealed record ContainerObservation(string Name, string State, int? ExitCode);
