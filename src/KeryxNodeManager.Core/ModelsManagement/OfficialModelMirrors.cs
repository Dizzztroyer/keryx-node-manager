using KeryxNodeManager.Core.Models;

namespace KeryxNodeManager.Core.ModelsManagement;

/// <summary>One officially-announced mirror set for a single <see cref="ModelTier"/>'s model
/// archive. Unlike <see cref="ModelCardViewModel"/>'s free-text "paste any URL you trust" field,
/// every URL here was individually verified live (real HTTP HEAD/Range requests, not assumed from
/// the announcement text) before being hardcoded - see the class doc comment below for the
/// verification method and date. <see cref="TorrentUrl"/> is null where no working torrent mirror
/// exists for that tier.</summary>
public sealed record OfficialModelMirror(ModelTier Tier, string DirectUrl, string? TorrentUrl);

/// <summary>
/// Real, live-verified official mirrors for each model tier's archive, as announced by the Keryx
/// Labs dev team in their Discord (two announcements, 2026-08-03: one via Hugging Face, one via
/// keryx-labs.com direct+torrent). Each archive's zip root folder matches the tier's
/// <see cref="ModelSpec.DirName"/> exactly (confirmed for GLM-4-9B-0414.zip by reading its real
/// remote central-directory via an HTTP Range request - "GLM-4-9B-0414/", "GLM-4-9B-0414/.ok",
/// "GLM-4-9B-0414/model.gguf" - so extracting straight into the configured models directory root
/// requires no renaming).
///
/// This deliberately does NOT just paste every URL from the Discord announcement: EXAONE-4.0-1.2B
/// (announced as the VeryLight tier's keryx-labs.com replacement) returned 404 on every filename
/// variant tried (both the .zip and the .torrent, on 2026-08-03) - it is not actually live yet
/// despite being announced, so VeryLight uses the Hugging Face mirror of the original
/// Qwen3-8B-abliterated model instead (confirmed live via a real 302 redirect to the CDN). If
/// keryx-labs.com's EXAONE-4.0-1.2B mirror goes live later, this table should be updated once that
/// is independently re-verified - not on the strength of a second announcement alone, per this
/// project's standing "never hardcode an unverified endpoint" rule.
///
/// The four keryx-labs.com tiers (Light/Default/High/VeryHigh) each have both a direct HTTP URL
/// and a .torrent metadata URL, both confirmed live (HTTP 200) on 2026-08-03. VeryLight has only
/// the Hugging Face direct URL - no torrent mirror was announced or found for it.
/// </summary>
public static class OfficialModelMirrors
{
    public static readonly IReadOnlyDictionary<ModelTier, OfficialModelMirror> Mirrors =
        new Dictionary<ModelTier, OfficialModelMirror>
        {
            [ModelTier.VeryLight] = new(
                ModelTier.VeryLight,
                "https://huggingface.co/datasets/Keryx-Labs/models/resolve/main/Qwen3-8B-abliterated.zip",
                null),
            [ModelTier.Light] = new(
                ModelTier.Light,
                "https://keryx-labs.com/Mistral-7B-v0.3.zip",
                "https://keryx-labs.com/Mistral-7B-v0.3.zip.torrent"),
            [ModelTier.Default] = new(
                ModelTier.Default,
                "https://keryx-labs.com/GLM-4-9B-0414.zip",
                "https://keryx-labs.com/GLM-4-9B-0414.zip.torrent"),
            [ModelTier.High] = new(
                ModelTier.High,
                "https://keryx-labs.com/Qwen3.6-27B.zip",
                "https://keryx-labs.com/Qwen3.6-27B.zip.torrent"),
            [ModelTier.VeryHigh] = new(
                ModelTier.VeryHigh,
                "https://keryx-labs.com/Kimi-Linear-48B.zip",
                "https://keryx-labs.com/Kimi-Linear-48B.zip.torrent"),
        };

    public static OfficialModelMirror? TryGet(ModelTier tier) =>
        Mirrors.TryGetValue(tier, out var mirror) ? mirror : null;
}
