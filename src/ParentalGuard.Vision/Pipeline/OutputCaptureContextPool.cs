namespace ParentalGuard.Vision.Pipeline;

/// <summary>
/// Architecture/05 mục 3.5 (ADR-65): quản lý vòng đời <see cref="OutputCaptureContext"/> theo
/// output — tạo lazy qua <c>createContext</c> khi 1 output có candidate lần đầu, dispose sau
/// <c>idleDisposeThreshold</c> chu kỳ capture LIÊN TIẾP không có candidate nào trên output đó.
/// Tách khỏi <see cref="CaptureLoopWorker"/> (seam <c>createContext</c>) để test được logic vòng đời
/// (idle counting/dispose/lazy-create) mà không cần D3D11/DXGI thật.
/// </summary>
public sealed class OutputCaptureContextPool
{
    private readonly Func<OutputCaptureContext?> _createContext;
    private readonly int _idleDisposeThreshold;
    private readonly Dictionary<int, OutputCaptureContext> _contexts;

    public OutputCaptureContextPool(Func<OutputCaptureContext?> createContext, int idleDisposeThreshold, int seedOutputIndex, OutputCaptureContext seedContext)
    {
        _createContext = createContext;
        _idleDisposeThreshold = idleDisposeThreshold;
        _contexts = new Dictionary<int, OutputCaptureContext> { [seedOutputIndex] = seedContext };
    }

    public int Count => _contexts.Count;

    public bool Contains(int outputIndex) => _contexts.ContainsKey(outputIndex);

    public int IdleCyclesFor(int outputIndex) => _contexts[outputIndex].IdleCycles;

    /// <summary>
    /// Trả về context hiện có (reset idle counter về 0) hoặc tạo mới lazy. <c>null</c> nếu tạo thất
    /// bại — caller bỏ qua candidate này chu kỳ này (fail-secure: tiếp tục giám sát output/candidate
    /// khác thay vì crash toàn bộ Vision, Architecture/01 mục 5).
    /// </summary>
    public OutputCaptureContext? GetOrCreate(int outputIndex)
    {
        if (_contexts.TryGetValue(outputIndex, out OutputCaptureContext? existing))
        {
            existing.IdleCycles = 0;
            return existing;
        }

        OutputCaptureContext? created = _createContext();
        if (created is not null)
        {
            _contexts[outputIndex] = created;
        }

        return created;
    }

    /// <summary>Gọi đúng 1 lần cuối mỗi chu kỳ capture — tăng idle counter cho context không dùng chu kỳ này, dispose khi vượt ngưỡng.</summary>
    public void EndCycle(IReadOnlySet<int> usedOutputIndexes)
    {
        List<int>? idleOutputIndexes = null;
        foreach ((int outputIndex, OutputCaptureContext context) in _contexts)
        {
            if (usedOutputIndexes.Contains(outputIndex))
            {
                continue;
            }

            context.IdleCycles++;
            if (context.IdleCycles >= _idleDisposeThreshold)
            {
                (idleOutputIndexes ??= []).Add(outputIndex);
            }
        }

        if (idleOutputIndexes is null)
        {
            return;
        }

        foreach (int outputIndex in idleOutputIndexes)
        {
            _contexts[outputIndex].Dispose();
            _contexts.Remove(outputIndex);
        }
    }

    public void DisposeAll()
    {
        foreach (OutputCaptureContext context in _contexts.Values)
        {
            context.Dispose();
        }

        _contexts.Clear();
    }
}
