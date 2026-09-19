using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace ParentalGuard.Vision.Inference;

/// <summary>
/// Bước 4 thật (Architecture/05 mục 4.3, ADR-44/45/48): sở hữu 1 <see cref="InferenceSession"/>
/// khởi tạo eager, tái dùng suốt vòng đời process — KHÔNG tạo session mới mỗi frame.
/// </summary>
public sealed class NsfwClassifier : INsfwClassifier
{
    private readonly InferenceSession _session;
    private readonly string _inputName;
    private readonly string _outputName;

    public TensorLayout InputLayout { get; }

    /// <summary>Model 5 lớp cố định 224×224×3 (`IMG-014`/`PERF-032`) — <paramref name="modelBytes"/> đã được caller nạp sẵn (đọc file 1 lần).</summary>
    public NsfwClassifier(byte[] modelBytes, SessionOptions sessionOptions)
    {
        ArgumentNullException.ThrowIfNull(modelBytes);
        ArgumentNullException.ThrowIfNull(sessionOptions);

        _session = new InferenceSession(modelBytes, sessionOptions);

        var inputMeta = _session.InputMetadata.First();
        _inputName = inputMeta.Key;
        InputLayout = DetermineLayout(inputMeta.Value.Dimensions);
        _outputName = _session.OutputMetadata.First().Key;
    }

    public NsfwClassProbabilities Classify(DenseTensor<float> input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var inputs = new List<NamedOnnxValue> { NamedOnnxValue.CreateFromTensor(_inputName, input) };
        using IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results = _session.Run(inputs, [_outputName]);
        float[] probabilities = results.First().AsEnumerable<float>().ToArray();
        try
        {
            if (probabilities.Length != 5)
            {
                throw new InvalidOperationException($"Expected 5 class probabilities from nsfw_model output, got {probabilities.Length}.");
            }

            // Thứ tự alphabet của GantMan/nsfw_model: drawing, hentai, neutral, porn, sexy (IMG-014).
            return new NsfwClassProbabilities(probabilities[0], probabilities[1], probabilities[2], probabilities[3], probabilities[4]);
        }
        finally
        {
            // IMG-003 — output tensor (bảng mục 6): zero ngay sau khi đọc xong.
            Array.Clear(probabilities);
        }
    }

    /// <summary>ADR-48: NCHW nếu trục thứ 2 (index 1, sau batch) bằng 3 kênh màu, ngược lại mặc định NHWC.</summary>
    private static TensorLayout DetermineLayout(int[] dimensions) =>
        dimensions.Length == 4 && dimensions[1] == 3 ? TensorLayout.Nchw : TensorLayout.Nhwc;

    public void Dispose() => _session.Dispose();
}
