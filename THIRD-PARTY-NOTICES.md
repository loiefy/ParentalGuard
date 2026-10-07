# Thành phần bên thứ ba / Third-party notices

ParentalGuard sử dụng các thành phần bên thứ ba dưới đây. Mỗi thành phần thuộc bản quyền của tác giả tương ứng và được
phân phối theo giấy phép ghi bên cạnh. Toàn văn các giấy phép nằm trong thư mục `third-party-licenses/` đi kèm bản phát hành
(và trong gói NuGet gốc của từng thành phần).

ParentalGuard uses the third-party components listed below. Each component is copyrighted by its respective authors and
distributed under the licence shown. Full licence texts are in the `third-party-licenses/` folder shipped with the release
(and in each component's original NuGet package).

## Mô hình AI / AI model

| Thành phần | Tác giả | Giấy phép | Nguồn |
|---|---|---|---|
| **Marqo/nsfw-image-detection-384** (mô hình nhận diện mặc định, chuyển sang ONNX bằng `tools/export_nsfw_models.py`; trọng số không bị sửa đổi, chỉ bọc thêm bước chuẩn hoá ảnh và softmax) | Marqo | Apache-2.0 | https://huggingface.co/Marqo/nsfw-image-detection-384 |
| timm `vit_tiny_patch16_384.augreg_in21k_ft_in1k` (mô hình nền của Marqo) | Ross Wightman / PyTorch Image Models | Apache-2.0 | https://github.com/huggingface/pytorch-image-models |

Các mô hình chỉ dùng cho so sánh/thử nghiệm (không đóng gói trong bản phát hành chuẩn): `GantMan/nsfw_model` (MIT,
https://github.com/GantMan/nsfw_model), `Falconsai/nsfw_image_detection` (Apache-2.0,
https://huggingface.co/Falconsai/nsfw_image_detection).

## Thư viện / Libraries

| Thành phần | Tác giả | Giấy phép |
|---|---|---|
| .NET runtime, Microsoft.Extensions.\*, Microsoft.Data.Sqlite, System.\* | .NET Foundation and Contributors | MIT |
| ONNX Runtime (Microsoft.ML.OnnxRuntime.DirectML) | Microsoft Corporation | MIT |
| DirectML (Microsoft.AI.DirectML) | Microsoft Corporation | Microsoft Software License Terms — DirectML (redistributable) |
| Windows App SDK (Microsoft.WindowsAppSDK) | Microsoft Corporation | Microsoft Software License Terms — Windows App SDK (redistributable) |
| CommunityToolkit.Mvvm | .NET Foundation and Contributors | MIT |
| Google.Protobuf | Google Inc. | BSD-3-Clause |
| Konscious.Security.Cryptography.Argon2 | Keef Aragon | MIT |
| Vortice.Direct3D11, Vortice.DXGI | Amer Koleci | MIT |
| Interop.UIAutomationClient | Roman (Roemer) | MIT |
| SQLitePCLRaw | Eric Sink / SourceGear | Apache-2.0 |
| SQLite | D. Richard Hipp và cộng sự | Public domain |
| PDFsharp | empira Software GmbH | MIT |

Công cụ chỉ dùng lúc build (không phân phối): Grpc.Tools (Apache-2.0), Microsoft.Windows.SDK.BuildTools (Microsoft).

## Văn bản giấy phép MIT / MIT licence text

Áp dụng cho mọi thành phần ghi "MIT" ở trên, với dòng bản quyền tương ứng của từng tác giả.

```
Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated
documentation files (the "Software"), to deal in the Software without restriction, including without limitation the
rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit
persons to whom the Software is furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all copies or substantial portions of the
Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE
WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR
COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR
OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
```

Toàn văn Apache-2.0: `third-party-licenses/Apache-2.0.txt`. BSD-3-Clause (Google.Protobuf): `third-party-licenses/BSD-3-Clause-protobuf.txt`.
