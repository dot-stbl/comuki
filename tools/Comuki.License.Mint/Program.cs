using System.Globalization;
using Comuki.Shared.Editions.Licensing.Ed25519;
using Comuki.Shared.Editions.Licensing.Grants;
using Comuki.Shared.Editions.Licensing.Modes;
using Comuki.Shared.Editions.Tiers;

namespace Comuki.License.Mint;

/// <summary>
/// Dev-only CLI around <see cref="Ed25519LicenseSigner"/>. Two
/// subcommands: <c>generate-key</c> mints a fresh Ed25519 keypair
/// (operator saves the public key to embed in production builds, the
/// private key stays on an offline signer); <c>sign</c> reads a private
/// key from a file and mints a license token shaped exactly like the one
/// production's <see cref="Ed25519LicenseSigner.Sign"/> would produce.
/// This tool never touches the production embedded key — it is a
/// hand-off companion for issuance, not a runtime dependency.
/// </summary>
public static class Program
{
    /// <summary>
    /// Entrypoint. First positional argument selects the subcommand:
    /// <c>generate-key</c> or <c>sign</c>. Unknown / missing subcommand
    /// prints a short usage block to stdout and returns 0 (the
    /// <c>--help</c> equivalent).
    /// </summary>
    /// <param name="args">Command-line arguments.</param>
    /// <returns>0 on success, 1 on any usage/parse/runtime error (a diagnostic lands on stderr in that case).</returns>
    public static int Main(string[] args)
    {
        try
        {
            if (args.Length == 0)
            {
                ProgramHelpers.PrintUsage();
                return 0;
            }

            return args[0] switch
            {
                "generate-key" => ProgramHelpers.RunGenerateKey(args),
                "sign" => ProgramHelpers.RunSign(args),
                _ => ProgramHelpers.RunUnknownSubcommand(args[0]),
            };
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"[Comuki.License.Mint] {exception.Message}");
            return 1;
        }
    }
}

/// <summary>
/// Pure-function helpers for <see cref="Program"/>. Lives in a
/// <c>file static class</c> so the entrypoint stays free of
/// <c>private</c> methods (project rule <c>code-shape.md</c> §9). Each
/// helper is independently testable and the file is the smallest
/// possible unit that holds the full CLI surface.
/// </summary>
file static class ProgramHelpers
{
    /// <summary>Marker used by <see cref="UsageBlock"/> — every subcommand line is one bullet in the printed help.</summary>
    private const string UsageBlock =
        "Usage:\n" +
        "  Comuki.License.Mint generate-key [--out-public <path>] [--out-private <path>]\n" +
        "  Comuki.License.Mint sign --private-key-file <path> --org <name> --edition <code> --expiry <iso8601> --mode <implicit-by-rank|explicit-allowlist> [--not-before <iso8601>] [--seats <int>] [--features <a,b,c>] [--limits <k=v,k2=v2>] [--out <path>]\n";

    /// <summary>Writes the two-line usage block to stdout.</summary>
    public static void PrintUsage()
    {
        Console.Write(UsageBlock);
    }

    /// <summary>Unknown subcommand — print a one-liner + usage block to stdout and return 0, mirroring the no-args path.</summary>
    /// <param name="unrecognised">The first positional argument the user passed.</param>
    public static int RunUnknownSubcommand(string unrecognised)
    {
        Console.WriteLine($"Unrecognised subcommand '{unrecognised}'.");
        PrintUsage();
        return 0;
    }

    /// <summary>
    /// <c>generate-key</c> subcommand. Mints a fresh Ed25519 keypair
    /// and either writes both halves to user-named files (exactly both,
    /// otherwise a usage error) or prints them labelled to stdout.
    /// </summary>
    /// <param name="args">Args following the <c>generate-key</c> verb.</param>
    public static int RunGenerateKey(string[] args)
    {
        var outPublic = ParseValue(args, "--out-public");
        var outPrivate = ParseValue(args, "--out-private");

        if ((outPublic is null) ^ (outPrivate is null))
        {
            Console.Error.WriteLine("[Comuki.License.Mint] --out-public and --out-private must be supplied together.");
            return 1;
        }

        var (publicKey, privateKeySeed) = Ed25519LicenseSigner.GenerateKeyPair();
        var publicEncoded = Convert.ToBase64String(publicKey);
        var privateEncoded = Convert.ToBase64String(privateKeySeed);

        if (outPublic is not null && outPrivate is not null)
        {
            File.WriteAllText(outPublic, publicEncoded);
            File.WriteAllText(outPrivate, privateEncoded);
            Console.WriteLine($"Wrote public key to {outPublic} and private key seed to {outPrivate}.");
            return 0;
        }

        Console.WriteLine($"PUBLIC KEY  (safe to embed/share): {publicEncoded}");
        Console.WriteLine($"PRIVATE KEY (keep secret, never commit, never paste in a ticket): {privateEncoded}");
        return 0;
    }

    /// <summary>
    /// <c>sign</c> subcommand. Reads the private-key seed from a file
    /// (never the inline base64), parses the remaining flags, and
    /// emits a token either to <c>--out</c> or stdout. Every parse
    /// error names the offending flag and value and returns 1.
    /// </summary>
    /// <param name="args">Args following the <c>sign</c> verb.</param>
    public static int RunSign(string[] args)
    {
        var privateKeyPath = RequireValue(args, "--private-key-file");
        if (privateKeyPath is null)
        {
            return 1;
        }

        byte[] privateKeySeed;
        try
        {
            privateKeySeed = Convert.FromBase64String(File.ReadAllText(privateKeyPath).Trim());
        }
        catch (FileNotFoundException)
        {
            Console.Error.WriteLine($"[Comuki.License.Mint] --private-key-file '{privateKeyPath}' does not exist.");
            return 1;
        }
        catch (FormatException)
        {
            Console.Error.WriteLine($"[Comuki.License.Mint] --private-key-file '{privateKeyPath}' is not a base64-encoded seed.");
            return 1;
        }

        var org = RequireValue(args, "--org");
        if (org is null)
        {
            return 1;
        }

        var editionRaw = RequireValue(args, "--edition");
        if (editionRaw is null)
        {
            return 1;
        }

        if (!EditionTiers.TryGetByCode(editionRaw, out var tier))
        {
            var codes = string.Join(", ", EditionTiers.All.Select(static t => t.Code));
            Console.Error.WriteLine($"[Comuki.License.Mint] --edition '{editionRaw}' is not a known tier. Known: {codes}.");
            return 1;
        }

        var expiryRaw = RequireValue(args, "--expiry");
        if (expiryRaw is null)
        {
            return 1;
        }

        DateTimeOffset expiry;
        try
        {
            expiry = ParseTimestamp(expiryRaw, "--expiry");
        }
        catch (FormatException exception)
        {
            Console.Error.WriteLine($"[Comuki.License.Mint] {exception.Message}");
            return 1;
        }

        var modeRaw = RequireValue(args, "--mode");
        if (modeRaw is null)
        {
            return 1;
        }

        if (!LicenseMode.TryParse(modeRaw, out var mode))
        {
            Console.Error.WriteLine($"[Comuki.License.Mint] --mode '{modeRaw}' is not recognised. Expected 'implicit-by-rank' or 'explicit-allowlist'.");
            return 1;
        }

        DateTimeOffset? notBefore = null;
        if (ParseValue(args, "--not-before") is { } notBeforeRaw)
        {
            try
            {
                notBefore = ParseTimestamp(notBeforeRaw, "--not-before");
            }
            catch (FormatException exception)
            {
                Console.Error.WriteLine($"[Comuki.License.Mint] {exception.Message}");
                return 1;
            }
        }

        int? seats = null;
        if (ParseValue(args, "--seats") is { } seatsRaw)
        {
            if (!int.TryParse(seatsRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedSeats))
            {
                Console.Error.WriteLine($"[Comuki.License.Mint] --seats '{seatsRaw}' is not an integer.");
                return 1;
            }

            seats = parsedSeats;
        }

        IReadOnlyCollection<string>? features = null;
        if (ParseValue(args, "--features") is { } featuresRaw)
        {
            features = [.. ParseCsv(featuresRaw)
                .Where(static segment => !string.IsNullOrWhiteSpace(segment))
                .Select(static segment => segment.Trim())
                .Where(static segment => segment.Length > 0)];
        }

        IReadOnlyDictionary<string, int>? limits = null;
        if (ParseValue(args, "--limits") is { } limitsRaw)
        {
            try
            {
                limits = ParseLimits(limitsRaw);
            }
            catch (FormatException exception)
            {
                Console.Error.WriteLine($"[Comuki.License.Mint] {exception.Message}");
                return 1;
            }
        }

        var grant = new LicenseGrant(
            Org: org,
            Tier: tier,
            Expiry: expiry,
            Mode: mode,
            NotBefore: notBefore,
            Seats: seats,
            Features: features,
            Limits: limits);

        var token = Ed25519LicenseSigner.Sign(grant, privateKeySeed);

        var outPath = ParseValue(args, "--out");
        if (outPath is not null)
        {
            File.WriteAllText(outPath, token);
            Console.WriteLine($"Wrote token to {outPath}.");
        }
        else
        {
            Console.Write(token);
        }

        return 0;
    }

    /// <summary>Returns the value following <paramref name="flag"/>, or <c>null</c> when the flag is absent.</summary>
    /// <param name="args">The full args array (the function finds the flag regardless of position).</param>
    /// <param name="flag">The flag to look up (without the leading <c>--</c>).</param>
    public static string? ParseValue(string[] args, string flag)
    {
        for (var index = 0; index < args.Length; index++)
        {
            if (args[index] == flag && index + 1 < args.Length)
            {
                return args[index + 1];
            }
        }

        return null;
    }

    /// <summary>
    /// Same as <see cref="ParseValue"/> but treats a missing flag as a
    /// usage error: writes a <c>[Comuki.License.Mint] --&lt;flag&gt; is required.</c>
    /// line to stderr and returns <c>null</c> so the caller can short-circuit with <c>return 1</c>.
    /// </summary>
    /// <param name="args">The full args array.</param>
    /// <param name="flag">The flag the user must have supplied.</param>
    public static string? RequireValue(string[] args, string flag)
    {
        var value = ParseValue(args, flag);
        if (value is null)
        {
            Console.Error.WriteLine($"[Comuki.License.Mint] --{flag.TrimStart('-')} is required.");
        }

        return value;
    }

    /// <summary>
    /// Parses an ISO-8601 timestamp string. Uses
    /// <see cref="DateTimeStyles.AssumeUniversal"/> so the resulting
    /// <see cref="DateTimeOffset"/> is always UTC regardless of whether
    /// the input carried a <c>Z</c> suffix or a numeric offset — the
    /// token wire format is UTC-by-convention.
    /// </summary>
    /// <param name="raw">The raw flag value.</param>
    /// <param name="flag">The flag name, included in the failure message.</param>
    /// <exception cref="FormatException"><paramref name="raw"/> does not parse as a timestamp.</exception>
    public static DateTimeOffset ParseTimestamp(string raw, string flag)
    {
        try
        {
            return DateTimeOffset.Parse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
        }
        catch (FormatException)
        {
            throw new FormatException($"--{flag.TrimStart('-')} '{raw}' is not a valid ISO-8601 timestamp.");
        }
    }

    /// <summary>Splits <paramref name="raw"/> on <c>,</c>, dropping empty segments.</summary>
    /// <param name="raw">The raw flag value.</param>
    public static IEnumerable<string> ParseCsv(string raw)
    {
        return raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    /// <summary>
    /// Parses the <c>--limits</c> flag value (<c>k1=v1,k2=v2</c>)
    /// into a dictionary. Each segment must contain exactly one
    /// <c>=</c>; both sides must be non-empty; the value must parse as
    /// an <see cref="int"/>.
    /// </summary>
    /// <param name="raw">The raw flag value.</param>
    /// <exception cref="FormatException">A segment is malformed or its value is not an integer.</exception>
    public static IReadOnlyDictionary<string, int> ParseLimits(string raw)
    {
        var result = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var segment in ParseCsv(raw))
        {
            var equalsIndex = segment.IndexOf('=');
            if (equalsIndex <= 0 || equalsIndex == segment.Length - 1)
            {
                throw new FormatException($"--limits segment '{segment}' must be 'key=value' with non-empty sides.");
            }

            var key = segment[..equalsIndex].Trim();
            var valueRaw = segment[(equalsIndex + 1)..].Trim();

            if (!int.TryParse(valueRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            {
                throw new FormatException($"--limits segment '{segment}' value is not an integer.");
            }

            result[key] = value;
        }

        return result;
    }
}
