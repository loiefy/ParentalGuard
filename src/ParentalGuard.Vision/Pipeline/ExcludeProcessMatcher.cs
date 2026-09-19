namespace ParentalGuard.Vision.Pipeline;

/// <summary>
/// Match tên process foreground với danh sách loại trừ (`BE-073`/`BE-073a`) do <c>Service</c> đẩy
/// xuống qua <c>ControlVisionCommand.exclude_process_names</c> — Vision không tự giữ danh sách.
/// </summary>
public static class ExcludeProcessMatcher
{
    /// <summary>So khớp không phân biệt hoa/thường (tên file Windows không phân biệt hoa/thường).</summary>
    public static bool IsExcluded(string? processName, IReadOnlyList<string> excludeProcessNames)
    {
        if (string.IsNullOrEmpty(processName) || excludeProcessNames.Count == 0)
        {
            return false;
        }

        foreach (string excluded in excludeProcessNames)
        {
            if (string.Equals(processName, excluded, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
