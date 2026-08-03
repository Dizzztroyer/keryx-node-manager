using System.Text.RegularExpressions;

namespace KeryxNodeManager.Core.Validation;

/// <summary>
/// Shallow, deliberately non-strict format check for a Keryx address. keryx-miner's cli.rs
/// hardcodes its devfund address as "keryx:qrxpcusyrxjxghfdumcxm2rqw4dhe3n9hyqpvgn2wfyldltf99w2xhnajuhte" —
/// a "keryx:" prefix followed by a lowercase bech32-charset payload (the codebase is a Kaspa
/// fork; Kaspa/Keryx addresses use bech32 without the numeric-only bech32 exclusions of 1,b,i,o).
/// This intentionally does NOT reimplement the real checksum algorithm from the keryx_addresses
/// Rust crate — a false "invalid" from a subtly wrong reimplementation is worse than accepting a
/// malformed address and letting the node's own startup error be authoritative
/// (docs/KERYX_RESEARCH.md §6).
/// </summary>
public static class KeryxAddressValidator
{
    // bech32 charset: qpzry9x8gf2tvdw0s3jn54khce6mua7l (no 1, b, i, o)
    private static readonly Regex Pattern = new(
        "^keryx(?:test)?:[qpzry9x8gf2tvdw0s3jn54khce6mua7l]{20,120}$",
        RegexOptions.Compiled);

    public static bool LooksValid(string? address)
    {
        if (string.IsNullOrWhiteSpace(address)) return false;
        return Pattern.IsMatch(address.Trim());
    }

    public static string? GetNetworkPrefix(string address)
    {
        var idx = address.IndexOf(':');
        return idx > 0 ? address[..idx] : null;
    }
}
