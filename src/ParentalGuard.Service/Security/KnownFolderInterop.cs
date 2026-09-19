using System.Runtime.InteropServices;

namespace ParentalGuard.Service.Security;

/// <summary>P/Invoke <c>SHGetKnownFolderPath</c> (shell32) — dùng để resolve Desktop của user đang ở session tương tác (ANTI-020, Architecture/09 mục 5.5 bước 2, ADR-97).</summary>
internal static partial class KnownFolderInterop
{
    internal static readonly Guid FolderIdDesktop = new("B4BFCC3A-DB2C-424C-B029-7FE99A87C641");

    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial int SHGetKnownFolderPath(in Guid rfid, uint dwFlags, IntPtr hToken, out IntPtr pszPath);

    [LibraryImport("ole32.dll")]
    internal static partial void CoTaskMemFree(IntPtr ptr);
}
