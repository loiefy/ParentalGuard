using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace ParentalGuard.Vision.Inference;

/// <summary>
/// `IMG-016c`: NudeNet v3.4 <c>640m.onnx</c> (YOLOv8m, 18 nhãn, AGPL-3.0 — https://github.com/notAI-tech/NudeNet). Output
/// <c>[1, 4 + 18, anchors]</c>: 4 giá trị khung (cx, cy, w, h) rồi điểm từng nhãn cho mỗi anchor. Chỉ cần độ tin cậy cao nhất,
/// nên không giải mã khung và không cần NMS. Session tạo 1 lần, tái dùng suốt vòng đời process (như <see cref="NsfwClassifier"/>).
/// </summary>
public sealed class NudeNetDetector : IBodyPartDetector
{
    /// <summary>Thứ tự nhãn của NudeNet v3 (<c>nudenet/nudenet.py</c>, <c>__labels</c>).</summary>
    internal static readonly string[] Labels =
    [
        "FEMALE_GENITALIA_COVERED", "FACE_FEMALE", "BUTTOCKS_EXPOSED", "FEMALE_BREAST_EXPOSED", "FEMALE_GENITALIA_EXPOSED",
        "MALE_BREAST_EXPOSED", "ANUS_EXPOSED", "FEET_EXPOSED", "BELLY_COVERED", "FEET_COVERED", "ARMPITS_COVERED",
        "ARMPITS_EXPOSED", "FACE_MALE", "BELLY_EXPOSED", "MALE_GENITALIA_EXPOSED", "ANUS_COVERED", "FEMALE_BREAST_COVERED",
        "BUTTOCKS_COVERED",
    ];

    /// <summary>`IMG-016c`: các nhãn tính là "bộ phận nhạy cảm lộ rõ".</summary>
    internal static readonly string[] ExposedLabels =
    [
        "FEMALE_GENITALIA_EXPOSED", "MALE_GENITALIA_EXPOSED", "FEMALE_BREAST_EXPOSED", "BUTTOCKS_EXPOSED", "ANUS_EXPOSED",
    ];

    private static readonly bool[] _isExposedClass = Labels.Select(label => ExposedLabels.Contains(label)).ToArray();

    private readonly InferenceSession _session;
    private readonly string _inputName;
    private readonly string _outputName;

    public int InputSize { get; }

    /// <summary><paramref name="modelBytes"/> đã được caller nạp + verify checksum (`MISC-090`).</summary>
    public NudeNetDetector(byte[] modelBytes, SessionOptions sessionOptions)
    {
        ArgumentNullException.ThrowIfNull(modelBytes);
        ArgumentNullException.ThrowIfNull(sessionOptions);

        _session = new InferenceSession(modelBytes, sessionOptions);

        var inputMeta = _session.InputMetadata.First();
        _inputName = inputMeta.Key;
        int[] dims = inputMeta.Value.Dimensions;
        InputSize = dims.Length == 4 && dims[2] > 0 ? dims[2] : 640;
        _outputName = _session.OutputMetadata.First().Key;
    }

    public float MaxExposedConfidence(DenseTensor<float> input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var inputs = new List<NamedOnnxValue> { NamedOnnxValue.CreateFromTensor(_inputName, input) };
        using IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results = _session.Run(inputs, [_outputName]);
        float[] output = results.First().AsEnumerable<float>().ToArray();
        try
        {
            return MaxExposedConfidence(output, Labels.Length);
        }
        finally
        {
            // IMG-003 — output tensor: zero ngay sau khi đọc xong.
            Array.Clear(output);
        }
    }

    /// <summary>
    /// Hàm thuần: <paramref name="output"/> là <c>[4 + classCount, anchors]</c> (đã bỏ trục batch). Giống NudeNet, mỗi anchor chỉ
    /// mang nhãn có điểm cao nhất của nó — anchor có nhãn cao nhất là "che" (vd ngực có áo) không tính dù điểm "lộ" cũng khá.
    /// </summary>
    internal static float MaxExposedConfidence(ReadOnlySpan<float> output, int classCount)
    {
        int channels = 4 + classCount;
        if (output.Length == 0 || output.Length % channels != 0)
        {
            throw new InvalidOperationException($"Unexpected detector output length {output.Length} for {channels} channels.");
        }

        int anchors = output.Length / channels;
        float best = 0f;
        for (int anchor = 0; anchor < anchors; anchor++)
        {
            int topClass = 0;
            float topScore = output[(4 * anchors) + anchor];
            for (int c = 1; c < classCount; c++)
            {
                float score = output[((4 + c) * anchors) + anchor];
                if (score > topScore)
                {
                    topScore = score;
                    topClass = c;
                }
            }

            if (_isExposedClass[topClass] && topScore > best)
            {
                best = topScore;
            }
        }

        return best;
    }

    public void Dispose() => _session.Dispose();
}
