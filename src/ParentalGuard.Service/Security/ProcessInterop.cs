using System.Runtime.InteropServices;
using System.Text;

namespace ParentalGuard.Service.Security;

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct StartupInfo
{
    public int Cb;
    public string? Reserved;
    public string? Desktop;
    public string? Title;
    public uint X;
    public uint Y;
    public uint XSize;
    public uint YSize;
    public uint XCountChars;
    public uint YCountChars;
    public uint FillAttribute;
    public uint Flags;
    public short ShowWindow;
    public short Reserved2;
    public IntPtr LpReserved2;
    public IntPtr StdInput;
    public IntPtr StdOutput;
    public IntPtr StdError;
}

/// <summary>
/// STARTUPINFOEX — StartupInfo cổ điển + con trỏ tới attribute list, dùng để giới hạn tập
/// handle được kế thừa qua <c>PROC_THREAD_ATTRIBUTE_HANDLE_LIST</c> (Architecture/03 mục 5.2,
/// ADR-18: không kế thừa "mọi handle inheritable hiện có", chỉ đúng 1 handle bootstrap pipe).
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct StartupInfoEx
{
    public StartupInfo StartupInfo;
    public IntPtr AttributeList;
}

[StructLayout(LayoutKind.Sequential)]
internal struct ProcessInformation
{
    public IntPtr Process;
    public IntPtr Thread;
    public uint ProcessId;
    public uint ThreadId;
}

/// <summary>
/// P/Invoke <c>CreateProcessAsUser</c> (BE-023a bước 6, Architecture/06 mục 2.1) — spawn
/// <c>Vision</c>/<c>Overlay</c> vào đúng session tương tác với restricted token, kèm cơ chế
/// giới hạn handle kế thừa (Architecture/03 mục 5.2, ADR-18).
/// </summary>
internal static class ProcessInterop
{
    internal const uint CreateUnicodeEnvironment = 0x00000400;
    internal const uint CreateNoWindow = 0x08000000;
    internal const uint ExtendedStartupinfoPresent = 0x00080000;
    internal const uint StartfUsestdhandles = 0x00000100;
    internal const int StdInputHandle = -10;

    private const uint _procThreadAttributeHandleList = 0x00020002;

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    internal static extern bool CreateProcessAsUser(
        IntPtr token,
        string? applicationName,
        StringBuilder commandLine,
        IntPtr processAttributes,
        IntPtr threadAttributes,
        [MarshalAs(UnmanagedType.Bool)] bool inheritHandles,
        uint creationFlags,
        IntPtr environment,
        string? currentDirectory,
        ref StartupInfoEx startupInfo,
        out ProcessInformation processInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool InitializeProcThreadAttributeList(IntPtr lpAttributeList, int dwAttributeCount, int dwFlags, ref IntPtr lpSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool UpdateProcThreadAttribute(
        IntPtr lpAttributeList,
        uint dwFlags,
        IntPtr attribute,
        IntPtr lpValue,
        IntPtr cbSize,
        IntPtr lpPreviousValue,
        IntPtr lpReturnSize);

    [DllImport("kernel32.dll")]
    private static extern bool DeleteProcThreadAttributeList(IntPtr lpAttributeList);

    /// <summary>
    /// Dựng attribute list giới hạn kế thừa đúng 1 handle (ADR-18) — caller sở hữu vòng đời
    /// đối tượng trả về (<see cref="ProcThreadAttributeList.Dispose"/> mới giải phóng bộ nhớ
    /// native + pin), phải Dispose sau khi <c>CreateProcessAsUser</c> trả về (thành công lẫn lỗi).
    /// </summary>
    internal static ProcThreadAttributeList CreateSingleHandleAttributeList(IntPtr inheritableHandle)
    {
        IntPtr size = IntPtr.Zero;
        InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref size);

        IntPtr attributeList = Marshal.AllocHGlobal(size);
        bool initialized = InitializeProcThreadAttributeList(attributeList, 1, 0, ref size);
        if (!initialized)
        {
            Marshal.FreeHGlobal(attributeList);
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "InitializeProcThreadAttributeList failed.");
        }

        GCHandle pinnedHandleArray = GCHandle.Alloc(new[] { inheritableHandle }, GCHandleType.Pinned);
        bool updated = UpdateProcThreadAttribute(
            attributeList,
            0,
            (IntPtr)_procThreadAttributeHandleList,
            pinnedHandleArray.AddrOfPinnedObject(),
            (IntPtr)IntPtr.Size,
            IntPtr.Zero,
            IntPtr.Zero);
        if (!updated)
        {
            int error = Marshal.GetLastWin32Error();
            pinnedHandleArray.Free();
            DeleteProcThreadAttributeList(attributeList);
            Marshal.FreeHGlobal(attributeList);
            throw new System.ComponentModel.Win32Exception(error, "UpdateProcThreadAttribute(PROC_THREAD_ATTRIBUTE_HANDLE_LIST) failed.");
        }

        return new ProcThreadAttributeList(attributeList, pinnedHandleArray);
    }

    internal sealed class ProcThreadAttributeList(IntPtr handle, GCHandle pinnedHandleArray) : IDisposable
    {
        public IntPtr Handle { get; } = handle;

        public void Dispose()
        {
            DeleteProcThreadAttributeList(Handle);
            Marshal.FreeHGlobal(Handle);
            pinnedHandleArray.Free();
        }
    }
}
