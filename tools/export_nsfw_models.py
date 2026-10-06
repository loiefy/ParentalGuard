"""
Tải + chuyển Marqo/nsfw-image-detection-384 và Falconsai/nsfw_image_detection (Apache-2.0) sang ONNX cho Vision
(cờ build ParentalGuardModel — xem src/Directory.Build.props). Mỗi file ONNX được BỌC sẵn:
  input  "pixels": float32 [1, 3, H, W], RGB, dải [0, 1]  (đúng dữ liệu FrameResizerNormalizer đang tạo)
  output "probs" : float32 [1, 2] = [P(bình thường), P(nsfw)]  (đã chuẩn hoá mean/std + softmax bên trong)
Cách chạy (cần torch, timm, transformers, onnx):  python tools/export_nsfw_models.py <thư_mục_đích>
"""
import sys
import torch
import timm
from transformers import AutoModelForImageClassification

out_dir = sys.argv[1] if len(sys.argv) > 1 else "models"


class Wrapped(torch.nn.Module):
    def __init__(self, core, mean, std, nsfw_index, hf):
        super().__init__()
        self.core = core
        self.hf = hf
        self.nsfw_index = nsfw_index
        self.register_buffer("mean", torch.tensor(mean).view(1, 3, 1, 1))
        self.register_buffer("std", torch.tensor(std).view(1, 3, 1, 1))

    def forward(self, pixels):
        x = (pixels - self.mean) / self.std
        logits = self.core(pixel_values=x).logits if self.hf else self.core(x)
        p = torch.softmax(logits, dim=-1)
        nsfw = p[:, self.nsfw_index:self.nsfw_index + 1]
        return torch.cat([1 - nsfw, nsfw], dim=1)


def export(module, size, path):
    module.eval()
    dummy = torch.rand(1, 3, size, size)
    torch.onnx.export(module, dummy, path, input_names=["pixels"], output_names=["probs"], opset_version=17, dynamo=False)
    print("exported", path)


# --- Marqo (timm ViT-tiny 384) ---
m = timm.create_model("hf-hub:Marqo/nsfw-image-detection-384", pretrained=True)
cfg = timm.data.resolve_data_config({}, model=m)
labels = m.pretrained_cfg.get("label_names") or m.pretrained_cfg.get("label_names_override")
print("marqo cfg", cfg, "labels", labels)
nsfw_idx = [i for i, l in enumerate(labels) if l.lower() == "nsfw"][0] if labels else 0
export(Wrapped(m, cfg["mean"], cfg["std"], nsfw_idx, hf=False), cfg["input_size"][1], f"{out_dir}/nsfw_marqo_384.onnx")

# --- Falconsai (HF ViT-base 224) ---
f = AutoModelForImageClassification.from_pretrained("Falconsai/nsfw_image_detection")
print("falconsai labels", f.config.id2label)
nsfw_idx = [i for i, l in f.config.id2label.items() if l.lower() == "nsfw"][0]
export(Wrapped(f, [0.5, 0.5, 0.5], [0.5, 0.5, 0.5], int(nsfw_idx), hf=True), 224, f"{out_dir}/nsfw_falconsai_224.onnx")
