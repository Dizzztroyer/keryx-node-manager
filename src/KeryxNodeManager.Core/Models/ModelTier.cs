namespace KeryxNodeManager.Core.Models;

/// <summary>
/// The five Proof-of-Model tiers the keryx-miner binary supports. One tier == one model,
/// tiers are NOT cumulative and VRAM is never pooled across GPUs.
/// Source: keryx-miner src/cli.rs (flag names) and src/models.rs (ModelSpec.min_vram_mb),
/// verified against the actual Rust source on 2026-08-02. See docs/KERYX_RESEARCH.md §2-3.
/// </summary>
public enum ModelTier
{
    VeryLight = 0,
    Light = 1,
    Default = 2,
    High = 3,
    VeryHigh = 4,
}

/// <summary>
/// Special value meaning "this GPU is excluded from mining entirely" — distinct from any real
/// tier. Kept out of the ModelTier enum itself so tier-indexed arrays/switches stay exhaustive.
/// </summary>
public static class GpuAssignmentMode
{
    public const string Auto = "auto";
    public const string Disabled = "disabled";
}

public sealed record ModelSpec(
    ModelTier Tier,
    string Name,
    string DirName,
    string Quantization,
    string CliFlag,
    long MinVramMb);

/// <summary>
/// The verified tier table. Numbers are the ModelSpec.min_vram_mb constants read directly out of
/// keryx-miner's src/models.rs — NOT the (slightly rounded) numbers in the miner's own README.
/// The top tier's real gate is 30,000 MB, not "32 GB+".
/// </summary>
public static class ModelTierCatalog
{
    public static readonly IReadOnlyList<ModelSpec> Tiers = new List<ModelSpec>
    {
        new(ModelTier.VeryLight, "Qwen3-8B-abliterated", "Qwen3-8B-abliterated", "Q4_K_S", "--very-light", 6_000),
        new(ModelTier.Light, "Mistral-7B-v0.3", "Mistral-7B-v0.3", "Q6_K", "--light", 8_000),
        new(ModelTier.Default, "GLM-4-9B-0414", "GLM-4-9B-0414", "Q6_K", "", 12_000),
        new(ModelTier.High, "Qwen3.6-27B", "Qwen3.6-27B", "Q4_K_M", "--high", 24_000),
        new(ModelTier.VeryHigh, "Kimi-Linear-48B", "Kimi-Linear-48B", "Q4_K_M", "--very-high", 30_000),
    };

    public static ModelSpec Get(ModelTier tier) => Tiers.First(t => t.Tier == tier);

    /// <summary>Tiers ordered from most to least VRAM-hungry — used by the auto-assigner.</summary>
    public static IEnumerable<ModelSpec> ByDescendingVram() => Tiers.OrderByDescending(t => t.MinVramMb);

    public static string ForceModelToken(ModelTier tier) => tier switch
    {
        ModelTier.VeryLight => "very-light",
        ModelTier.Light => "light",
        ModelTier.Default => "default",
        ModelTier.High => "high",
        ModelTier.VeryHigh => "very-high",
        _ => throw new ArgumentOutOfRangeException(nameof(tier)),
    };
}
