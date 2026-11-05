using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace WslcDesktop.Compose.Parsing;

/// <summary>Thrown for compose content that can't be parsed or translated.</summary>
public sealed class ComposeException(string message) : Exception(message);
