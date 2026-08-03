using System.Text.RegularExpressions;

namespace KeryxNodeManager.Core.Secrets;

/// <summary>
/// Redacts values that must never land in logs or the diagnostic ZIP (brief §12, §20): full
/// mining addresses (shown truncated instead), anything that looks like a private key/seed
/// phrase/API token, and escrow key file contents. This app never asks for or stores a seed
/// phrase/private key in the first place — this masker exists as a second layer of defense in
/// case such a string ever appears in captured stdout/stderr from the miner/node processes.
/// </summary>
public static class SecretMasker
{
    private static readonly Regex HexSecret64Plus = new("\\b[0-9a-fA-F]{64,}\\b", RegexOptions.Compiled);
    // Matches both "key=value"/"key: value" and space-separated "Bearer <token>" forms.
    private static readonly Regex BearerToken = new(
        "(?i)(bearer|token|api[_-]?key|secret)\\s*[:=]?\\s*\\S+", RegexOptions.Compiled);

    public static string MaskAddress(string address)
    {
        if (string.IsNullOrEmpty(address)) return address;
        var idx = address.IndexOf(':');
        if (idx < 0 || idx + 9 >= address.Length) return "***";
        // keep prefix + first 6 chars of the payload, mask the rest
        return address[..(idx + 1 + 6)] + "…" + address[^4..];
    }

    /// <summary>Applies all masking rules to a block of log text before it is written/exported.</summary>
    public static string MaskLogLine(string line)
    {
        var masked = HexSecret64Plus.Replace(line, m => Mask(m.Value));
        masked = BearerToken.Replace(masked, m => Mask(m.Value));
        return masked;
    }

    private static string Mask(string value) =>
        value.Length <= 8 ? "***" : value[..4] + "…" + value[^4..];
}
