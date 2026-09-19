namespace ParentalGuard.Vision.Pipeline;

/// <summary>
/// Architecture/05-image-pipeline-architecture.md mục 9 điểm 3: hook chỉ dùng bởi test harness để
/// đếm số lần mỗi buffer được zero-out, đối chiếu đúng số lần buffer được cấp phát/dùng trong 1
/// phiên (không buffer nào "dùng xong mà không zero"). No-op trong Production (<see cref="NullFrameBufferAuditor"/>).
/// </summary>
public interface IFrameBufferAuditor
{
    /// <summary>Đối chiếu với <see cref="OnZeroed"/> trong test — mỗi lần cấp phát thật (không tính tái dùng) phải có đúng số lần zero tương ứng trước khi buffer rời scope.</summary>
    void OnBufferAllocated(string bufferId, int sizeBytes);

    void OnZeroed(string bufferId, int sizeBytes);
}

public sealed class NullFrameBufferAuditor : IFrameBufferAuditor
{
    public static readonly NullFrameBufferAuditor Instance = new();

    private NullFrameBufferAuditor()
    {
    }

    public void OnBufferAllocated(string bufferId, int sizeBytes)
    {
    }

    public void OnZeroed(string bufferId, int sizeBytes)
    {
    }
}
