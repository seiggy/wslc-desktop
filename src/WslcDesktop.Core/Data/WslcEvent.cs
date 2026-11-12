using System.Text.Json;

namespace WslcDesktop.Data;

/// <summary>One line of <c>wslc events --format json</c>: what changed (container, network, volume…) and how.</summary>
public sealed record WslcEvent(string Type, string Action, string Id, string Name)
{
    /// <summary>Parses an event line; null for anything else the stream prints (errors, blank lines).</summary>
    public static WslcEvent? Parse(string line)
    {
        var text = line.Trim();
        if (!text.StartsWith('{'))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;
            var type = Str(root, "Type");
            // Exec actions read like "exec_start: sh"; the part before the colon is the action.
            var action = Str(root, "Action");
            var colon = action.IndexOf(':');
            action = (colon >= 0 ? action[..colon] : action).Trim();
            if (type.Length == 0 || action.Length == 0)
            {
                return null;
            }

            var actor = root.TryGetProperty("Actor", out var a) && a.ValueKind == JsonValueKind.Object ? a : default;
            var id = Str(actor, "ID");
            var name = actor.ValueKind == JsonValueKind.Object && actor.TryGetProperty("Attributes", out var attrs) ? Str(attrs, "name") : string.Empty;
            return new WslcEvent(type, action, id, name.Length > 0 ? name : id);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() ?? string.Empty
            : string.Empty;
}
