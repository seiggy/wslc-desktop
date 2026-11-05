using System.Text.Json;

namespace WslcDesktop.Resources;

public sealed record ResourceRow(string Name, string Col1, string Col2, string Col3, bool CanRemove, IReadOnlyList<string> UsedBy);
