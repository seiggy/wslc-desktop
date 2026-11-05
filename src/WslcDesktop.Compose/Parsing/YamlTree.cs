using WslcDesktop.Localization;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace WslcDesktop.Compose.Parsing;

/// <summary>Parses YAML into plain dictionaries, lists and strings, resolving anchors, aliases and merge keys.</summary>
public static class YamlTree
{
    public static Dictionary<string, object?> ParseDocument(string yaml)
    {
        var stream = new YamlStream();
        try
        {
            stream.Load(new StringReader(yaml));
        }
        catch (YamlException e)
        {
            throw new ComposeException(UiStrings.Format("TheComposeFileIsnTValidYAML", e.Message));
        }

        if (stream.Documents.Count == 0)
        {
            return [];
        }

        return Convert(stream.Documents[0].RootNode) as Dictionary<string, object?>
            ?? throw new ComposeException(UiStrings.Get("TheComposeFileMustContainAMappingAtThe"));
    }

    private static object? Convert(YamlNode node)
    {
        switch (node)
        {
            case YamlMappingNode map:
            {
                var result = new Dictionary<string, object?>(StringComparer.Ordinal);
                foreach (var (k, v) in map.Children)
                {
                    var key = (k as YamlScalarNode)?.Value ?? string.Empty;
                    if (key == "<<")
                    {
                        foreach (var source in v is YamlSequenceNode seq ? seq.Children : [v])
                        {
                            if (Convert(source) is Dictionary<string, object?> merged)
                            {
                                foreach (var (mk, mv) in merged)
                                {
                                    result.TryAdd(mk, mv);
                                }
                            }
                        }
                    }
                }

                foreach (var (k, v) in map.Children)
                {
                    var key = (k as YamlScalarNode)?.Value ?? string.Empty;
                    if (key != "<<")
                    {
                        result[key] = Convert(v);
                    }
                }

                return result;
            }

            case YamlSequenceNode seq:
                return seq.Children.Select(Convert).ToList();

            case YamlScalarNode scalar:
                // Plain null scalars ("~", "null", empty) are real nulls; quoted ones are strings.
                if (scalar.Style == ScalarStyle.Plain
                    && (string.IsNullOrEmpty(scalar.Value) || scalar.Value is "~" or "null" or "Null" or "NULL"))
                {
                    return null;
                }

                return scalar.Value ?? string.Empty;

            default:
                return null;
        }
    }
}
