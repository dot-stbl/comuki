using System.Security.Cryptography;

namespace Comuki.Modules.Intake.Application.Sources;

/// <summary>
/// Webhook key generator — the unguessable routing segment of a
/// connection's hook URL (<c>/api/hooks/{provider}/{key}</c>). 16
/// lowercase alphanumerics from the cryptographic RNG (~96 bits with a
/// 32-symbol alphabet — enough for a URL secret that is additionally
/// backed by the signature check).
/// </summary>
public static class WebhookKeyGenerator
{
    private const int Length = 16;
    private const string Alphabet = "abcdefghijklmnopqrstuvwxyz0123456789";

    /// <summary>Generates a new webhook key.</summary>
    /// <returns></returns>
    public static string Generate()
    {
        var bytes = RandomNumberGenerator.GetBytes(Length);
        var chars = new char[Length];

        for (var index = 0; index < Length; index++)
        {
            chars[index] = Alphabet[bytes[index] % Alphabet.Length];
        }

        return new string(chars);
    }
}
