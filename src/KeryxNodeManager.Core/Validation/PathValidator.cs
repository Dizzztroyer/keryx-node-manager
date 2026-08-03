using KeryxNodeManager.Core.Localization;

namespace KeryxNodeManager.Core.Validation;

public sealed record PathValidationResult(bool IsValid, string? Error);

/// <summary>
/// Validates user-chosen directories (models dir, data dir, logs dir, etc.) before they are
/// persisted or passed to a child process. Rejects invalid path characters, relative paths, and
/// paths pointing at protected system locations — a directory picker dialog constrains most of
/// this already, but profile import (brief §15: "don't trust arbitrary shell commands on
/// import") and manual JSON edits do not go through the dialog.
/// </summary>
public static class PathValidator
{
    private static readonly string[] ProtectedRoots =
    {
        Environment.GetFolderPath(Environment.SpecialFolder.Windows),
        Environment.GetFolderPath(Environment.SpecialFolder.System),
    };

    public static PathValidationResult Validate(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return new PathValidationResult(false, CoreStrings.Get("Path.Empty"));

        if (path.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
            return new PathValidationResult(false, CoreStrings.Get("Path.InvalidChars"));

        if (!Path.IsPathRooted(path))
            return new PathValidationResult(false, CoreStrings.Get("Path.NotAbsolute"));

        string full;
        try
        {
            full = Path.GetFullPath(path);
        }
        catch (Exception ex)
        {
            return new PathValidationResult(false, CoreStrings.Format("Path.InvalidPath", ex.Message));
        }

        foreach (var root in ProtectedRoots)
        {
            if (!string.IsNullOrEmpty(root) &&
                full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                return new PathValidationResult(false,
                    CoreStrings.Format("Path.ProtectedRoot", root));
            }
        }

        return new PathValidationResult(true, null);
    }
}
