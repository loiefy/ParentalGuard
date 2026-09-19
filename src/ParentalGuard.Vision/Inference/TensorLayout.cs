namespace ParentalGuard.Vision.Inference;

/// <summary>ADR-48: layout trục tensor input, xác định động từ <c>session.InputMetadata</c> lúc khởi tạo, không hard-code.</summary>
public enum TensorLayout
{
    /// <summary>[batch, height, width, channels] — mặc định Keras/TensorFlow.</summary>
    Nhwc,

    /// <summary>[batch, channels, height, width] — nếu bước convert <c>tf2onnx</c> có transpose.</summary>
    Nchw,
}
