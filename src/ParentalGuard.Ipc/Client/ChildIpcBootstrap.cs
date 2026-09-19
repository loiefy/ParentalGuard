using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace ParentalGuard.Ipc.Client;

/// <summary>
/// Dữ liệu bootstrap mà <c>Service</c> ghi vào anonymous pipe kế thừa handle ngay sau khi
/// spawn <c>Vision</c>/<c>Overlay</c> (Architecture/03-ipc-communication.md mục 5.2, ADR-18):
/// tên named pipe nghiệp vụ, protocol version, và khoá HMAC 32 byte. Đọc đúng 1 lần lúc khởi
/// động, sau đó tiến trình con đóng hẳn handle bootstrap — không giữ lại, không log ra bất kỳ đâu.
///
/// Handle được Service truyền qua slot StdInput chuẩn của STARTUPINFO (không qua command-line/
/// biến môi trường — 2 kênh này đọc được từ tiến trình khác cùng user qua
/// NtQueryInformationProcess/PEB, đúng lý do ADR-18 cấm), con tự đọc lại bằng
/// <c>GetStdHandle(STD_INPUT_HANDLE)</c>, không cần Service truyền số hiệu handle qua đâu cả.
/// </summary>
public sealed record ChildIpcBootstrap(string PipeName, uint ProtocolVersion, byte[] HmacKey)
{
    private const int _hmacKeyLength = 32;
    private const int _stdInputHandle = -10;

    // CA5392: giới hạn tìm kernel32.dll trong System32, không dò theo PATH/thư mục app.
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetStdHandle(int nStdHandle);

    public static void WriteTo(AnonymousPipeServerStream serverStream, string pipeName, uint protocolVersion, byte[] hmacKey)
    {
        ArgumentNullException.ThrowIfNull(serverStream);
        ArgumentNullException.ThrowIfNull(pipeName);
        ArgumentNullException.ThrowIfNull(hmacKey);
        if (hmacKey.Length != _hmacKeyLength)
        {
            throw new ArgumentException($"HMAC key must be {_hmacKeyLength} bytes.", nameof(hmacKey));
        }

        using var writer = new BinaryWriter(serverStream, Encoding.UTF8, leaveOpen: true);
        byte[] pipeNameBytes = Encoding.UTF8.GetBytes(pipeName);
        writer.Write(pipeNameBytes.Length);
        writer.Write(pipeNameBytes);
        writer.Write(protocolVersion);
        writer.Write(hmacKey);
        writer.Flush();
    }

    public static ChildIpcBootstrap ReadFromInheritedStdHandle()
    {
        IntPtr rawHandle = GetStdHandle(_stdInputHandle);
        if (rawHandle == IntPtr.Zero || rawHandle == new IntPtr(-1))
        {
            throw new InvalidOperationException("Không tìm thấy bootstrap pipe handle ở STD_INPUT_HANDLE — tiến trình này phải được Service spawn qua ChildProcessLauncher.");
        }

        var safeHandle = new SafePipeHandle(rawHandle, ownsHandle: true);
        using var clientStream = new AnonymousPipeClientStream(PipeDirection.In, safeHandle);
        using var reader = new BinaryReader(clientStream, Encoding.UTF8, leaveOpen: true);
        int pipeNameLength = reader.ReadInt32();
        string pipeName = Encoding.UTF8.GetString(reader.ReadBytes(pipeNameLength));
        uint protocolVersion = reader.ReadUInt32();
        byte[] hmacKey = reader.ReadBytes(_hmacKeyLength);
        return new ChildIpcBootstrap(pipeName, protocolVersion, hmacKey);
    }
}
