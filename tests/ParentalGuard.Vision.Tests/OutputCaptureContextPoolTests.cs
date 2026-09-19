using ParentalGuard.Vision.Capture;
using ParentalGuard.Vision.Pipeline;

namespace ParentalGuard.Vision.Tests;

/// <summary>Architecture/05 mục 3.5 (ADR-65) — lazy create + dispose sau 5 chu kỳ idle liên tiếp, không cần D3D11/DXGI thật.</summary>
public class OutputCaptureContextPoolTests
{
    private const int _idleDisposeThreshold = 5;

    private static OutputCaptureContext NewFakeContext() => new(new FakeFrameCapture(), new FakeWindowCropper());

    [Fact]
    public void GetOrCreate_SeedOutput_ReturnsSeedWithoutCallingFactory()
    {
        var seed = NewFakeContext();
        var pool = new OutputCaptureContextPool(() => throw new InvalidOperationException("Không được gọi cho seed output."), _idleDisposeThreshold, seedOutputIndex: 0, seed);

        OutputCaptureContext? result = pool.GetOrCreate(0);

        Assert.Same(seed, result);
        Assert.Equal(1, pool.Count);
    }

    [Fact]
    public void GetOrCreate_NewOutput_LazilyCreatesViaFactory()
    {
        var created = NewFakeContext();
        int factoryCallCount = 0;
        var pool = new OutputCaptureContextPool(() => { factoryCallCount++; return created; }, _idleDisposeThreshold, seedOutputIndex: 0, NewFakeContext());

        OutputCaptureContext? result = pool.GetOrCreate(1);

        Assert.Same(created, result);
        Assert.Equal(1, factoryCallCount);
        Assert.Equal(2, pool.Count);
    }

    [Fact]
    public void GetOrCreate_FactoryFails_ReturnsNull_DoesNotAddToPool()
    {
        var pool = new OutputCaptureContextPool(() => null, _idleDisposeThreshold, seedOutputIndex: 0, NewFakeContext());

        OutputCaptureContext? result = pool.GetOrCreate(1);

        Assert.Null(result);
        Assert.Equal(1, pool.Count); // chỉ còn seed
        Assert.False(pool.Contains(1));
    }

    [Fact]
    public void GetOrCreate_ExistingContext_ResetsIdleCyclesToZero()
    {
        var pool = new OutputCaptureContextPool(() => throw new InvalidOperationException(), _idleDisposeThreshold, seedOutputIndex: 0, NewFakeContext());
        pool.EndCycle(usedOutputIndexes: new HashSet<int>()); // 1 chu kỳ idle
        pool.EndCycle(usedOutputIndexes: new HashSet<int>()); // 2 chu kỳ idle
        Assert.Equal(2, pool.IdleCyclesFor(0));

        pool.GetOrCreate(0);

        Assert.Equal(0, pool.IdleCyclesFor(0));
    }

    [Fact]
    public void EndCycle_UsedOutput_DoesNotIncrementIdleCounter()
    {
        var pool = new OutputCaptureContextPool(() => throw new InvalidOperationException(), _idleDisposeThreshold, seedOutputIndex: 0, NewFakeContext());

        pool.EndCycle(usedOutputIndexes: new HashSet<int> { 0 });

        Assert.Equal(0, pool.IdleCyclesFor(0));
        Assert.Equal(1, pool.Count);
    }

    [Fact]
    public void EndCycle_BelowThreshold_KeepsContextAlive()
    {
        var pool = new OutputCaptureContextPool(() => throw new InvalidOperationException(), _idleDisposeThreshold, seedOutputIndex: 0, NewFakeContext());

        for (int i = 0; i < _idleDisposeThreshold - 1; i++)
        {
            pool.EndCycle(usedOutputIndexes: new HashSet<int>());
        }

        Assert.Equal(1, pool.Count);
        Assert.True(pool.Contains(0));
    }

    [Fact]
    public void EndCycle_ReachesThreshold_DisposesAndRemovesContext()
    {
        var capture = new FakeFrameCapture();
        var cropper = new FakeWindowCropper();
        var seed = new OutputCaptureContext(capture, cropper);
        var pool = new OutputCaptureContextPool(() => throw new InvalidOperationException(), _idleDisposeThreshold, seedOutputIndex: 0, seed);

        for (int i = 0; i < _idleDisposeThreshold; i++)
        {
            pool.EndCycle(usedOutputIndexes: new HashSet<int>());
        }

        Assert.Equal(0, pool.Count);
        Assert.False(pool.Contains(0));
        Assert.True(capture.Disposed);
        Assert.True(cropper.Disposed);
    }

    [Fact]
    public void EndCycle_UsedAgainBeforeThreshold_NeverDisposed()
    {
        var capture = new FakeFrameCapture();
        var seed = new OutputCaptureContext(capture, new FakeWindowCropper());
        var pool = new OutputCaptureContextPool(() => throw new InvalidOperationException(), _idleDisposeThreshold, seedOutputIndex: 0, seed);

        pool.EndCycle(usedOutputIndexes: new HashSet<int>()); // idle 1
        pool.EndCycle(usedOutputIndexes: new HashSet<int>()); // idle 2
        pool.EndCycle(usedOutputIndexes: new HashSet<int> { 0 }); // dùng lại — reset về 0
        pool.EndCycle(usedOutputIndexes: new HashSet<int>()); // idle 1
        pool.EndCycle(usedOutputIndexes: new HashSet<int>()); // idle 2

        Assert.Equal(1, pool.Count);
        Assert.False(capture.Disposed);
    }

    [Fact]
    public void DisposeAll_DisposesEveryContextAndClearsPool()
    {
        var seedCapture = new FakeFrameCapture();
        var seedCropper = new FakeWindowCropper();
        var seed = new OutputCaptureContext(seedCapture, seedCropper);
        var createdCapture = new FakeFrameCapture();
        var createdCropper = new FakeWindowCropper();
        var pool = new OutputCaptureContextPool(() => new OutputCaptureContext(createdCapture, createdCropper), _idleDisposeThreshold, seedOutputIndex: 0, seed);
        pool.GetOrCreate(1);

        pool.DisposeAll();

        Assert.Equal(0, pool.Count);
        Assert.True(seedCapture.Disposed);
        Assert.True(seedCropper.Disposed);
        Assert.True(createdCapture.Disposed);
        Assert.True(createdCropper.Disposed);
    }

    private sealed class FakeFrameCapture : IFrameCapture
    {
        public bool Disposed { get; private set; }

        public IDisposable? AcquireNextFrame(int adapterIndex, int outputIndex, uint timeoutMs) => null;

        public void ReleaseFrame()
        {
        }

        public void Dispose() => Disposed = true;
    }

    private sealed class FakeWindowCropper : IWindowCropper
    {
        public bool Disposed { get; private set; }

        public void CropAndReadBack(IDisposable fullScreenFrame, WindowRect cropRect, byte[] destinationBgra8)
        {
        }

        public void Dispose() => Disposed = true;
    }
}
