using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace WslcDesktop.Compose.Secrets;

/// <summary>Generates random values for password-like secrets.</summary>
public static class SecretGenerator
{
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789";

    public static string Create(int length = 32)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(length, 8);
        return System.Security.Cryptography.RandomNumberGenerator.GetString(Alphabet, length);
    }
}
