namespace ParentalGuard.Uninstaller;

/// <summary>Seam test-only — tách thao tác OS thật (xoá file/registry/self-delete) khỏi <see cref="UninstallFlow"/>.</summary>
public interface ILocalCleanup
{
    /// <summary>Mục 5.5 bước 10-13 — CHỈ được gọi sau khi đã nhận <c>UninstallExecuteResponse{SUCCESS}</c> (ADR-98).</summary>
    Task CleanupAsync(CancellationToken cancellationToken);
}
