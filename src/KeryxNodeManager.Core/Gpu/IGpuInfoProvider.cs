using KeryxNodeManager.Core.Models;

namespace KeryxNodeManager.Core.Gpu;

public interface IGpuInfoProvider
{
    Task<IReadOnlyList<GpuDevice>> QueryAsync(CancellationToken ct = default);
}
