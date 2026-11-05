using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace WslcDesktop.Compose.Secrets.Injection;

/// <summary>A wslc invocation whose secret values travel in the child's environment (or a short-lived env file), not on the command line.</summary>
public sealed class PreparedCommand(IReadOnlyList<string> args, IReadOnlyDictionary<string, string> environment, IReadOnlyList<string> tempFiles) : IDisposable
{
    public IReadOnlyList<string> Args { get; } = args;

    /// <summary>Extra variables for the wslc process; <c>--env NAME</c> makes wslc pass the inherited value to the container.</summary>
    public IReadOnlyDictionary<string, string> Environment { get; } = environment;

    public void Dispose()
    {
        foreach (var file in tempFiles)
        {
            try
            {
                File.Delete(file);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
