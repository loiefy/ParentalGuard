using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Google.Protobuf;

namespace ParentalGuard.Ipc.Security;

/// <summary>
/// Memory hygiene cho field credential kiểu <c>bytes</c> trong IPC (Architecture/08-password-authentication-architecture.md
/// mục 5.2, ADR-73). <see cref="ByteString"/> nội bộ bất biến — <see cref="UnsafeGetBuffer"/> lấy
/// TRỰC TIẾP buffer nội bộ (không copy thêm trong trường hợp thường gặp), caller PHẢI gọi
/// <see cref="Zero"/> ngay sau khi dùng xong (thành công hay thất bại). Đây là biện pháp giảm
/// thiểu, không loại bỏ hoàn toàn được giới hạn của Protobuf serialization (residual risk đã ghi
/// nhận minh bạch ở Architecture/08 mục 5.2).
///
/// <para>
/// GHI CHÚ TRIỂN KHAI (lệch nhỏ so với văn bản Architecture/08 — đã xác nhận không phải gap WHAT):
/// Architecture/08 mục 5.2 nêu tên API <c>Google.Protobuf.UnsafeByteOperations.UnsafeGetBuffer</c>.
/// API đó KHÔNG tồn tại trong <c>Google.Protobuf 3.32.1</c> (phiên bản đã pin toàn dự án từ Đợt 0,
/// xác nhận qua reflection lúc implement) — bản hiện tại chỉ có <c>UnsafeWrap</c> (chiều ngược lại:
/// bọc <c>byte[]</c> có sẵn thành <see cref="ByteString"/> không copy). Thay thế tương đương cùng
/// mục đích (lấy buffer nội bộ, không copy thêm, có thể zero được) bằng
/// <see cref="MemoryMarshal.AsMemory{T}(ReadOnlyMemory{T})"/> trên <see cref="ByteString.Memory"/>
/// (tái diễn giải <c>ReadOnlyMemory&lt;byte&gt;</c> thành <c>Memory&lt;byte&gt;</c> ghi được, cùng
/// mảng lưu trữ — đã verify bằng thực nghiệm: zero qua handle này zero đúng dữ liệu
/// <see cref="ByteString"/> trả về sau đó) rồi <see cref="MemoryMarshal.TryGetArray{T}"/> để lấy
/// <c>byte[]</c> (Konscious Argon2 constructor yêu cầu đúng kiểu <c>byte[]</c>, không nhận <c>Span</c>).
/// </para>
/// </summary>
public static class CredentialBytes
{
    public static byte[] UnsafeGetBuffer(ByteString byteString)
    {
        Memory<byte> mutable = MemoryMarshal.AsMemory(byteString.Memory);
        if (MemoryMarshal.TryGetArray(mutable, out ArraySegment<byte> segment) && segment.Offset == 0 && segment.Count == byteString.Length)
        {
            return segment.Array!;
        }

        // Fallback an toàn nếu 1 bản Google.Protobuf tương lai đổi cách lưu trữ nội bộ ByteString
        // (không còn đúng 1 mảng liền mạch từ offset 0) — chấp nhận thêm 1 bản copy thay vì throw,
        // vẫn zero được bản copy trả về (mất phần "không copy thêm" của ADR-73, không mất tính đúng).
        return byteString.ToByteArray();
    }

    public static void Zero(byte[] buffer) => CryptographicOperations.ZeroMemory(buffer);
}
