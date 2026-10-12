using ParentalGuard.Ipc.Tamper;

namespace ParentalGuard.Service.Tests;

/// <summary>Mục 3.4 bước 4 (ADR-88) — phần thuần test được không cần SCM thật: resolve đường dẫn runtime + build tham số <c>sc.exe create</c>.</summary>
public class PeerServiceRecoveryTests
{
    [Fact]
    public void ResolvePeerBinaryPath_UsesOwnProcessDirectory_NotHardcodedAbsolutePath()
    {
        string path = PeerServiceRecovery.ResolvePeerBinaryPath("ParentalGuard.Watchdog.exe");

        string expectedDirectory = Path.GetDirectoryName(Environment.ProcessPath)!;
        Assert.Equal(Path.Combine(expectedDirectory, "ParentalGuard.Watchdog.exe"), path);
    }

    [Fact]
    public void BuildCreateArguments_ContainsAutoStartAndLocalSystem_NoDelayedStart()
    {
        string args = PeerServiceRecovery.BuildCreateArguments("ParentalGuardWatchdog", "ParentalGuard Watchdog", @"C:\Program Files\ParentalGuard\ParentalGuard.Watchdog.exe");

        Assert.Contains("create ParentalGuardWatchdog", args);
        // Ngoặc kép phải nằm TRONG giá trị binPath (sc.exe bỏ cặp ngoặc bao ngoài) — tránh "unquoted service path".
        Assert.Contains(@"binPath= ""\""C:\Program Files\ParentalGuard\ParentalGuard.Watchdog.exe\""""", args);
        Assert.Contains("start= auto", args); // SERVICE_AUTO_START, không Delayed (02 mục 2.1)
        Assert.Contains("obj= LocalSystem", args);
        Assert.Contains(@"DisplayName= ""ParentalGuard Watchdog""", args);
    }

    [Fact]
    public void BuildCreateArguments_BinPathValue_KeepsQuotesAfterWindowsArgumentParsing()
    {
        string args = PeerServiceRecovery.BuildCreateArguments("ParentalGuardWatchdog", "ParentalGuard Watchdog", @"C:\Program Files\ParentalGuard\ParentalGuard.Watchdog.exe");

        string[] argv = CommandLineToArgs("sc.exe " + args);
        int i = Array.IndexOf(argv, "binPath=");

        Assert.Equal(@"""C:\Program Files\ParentalGuard\ParentalGuard.Watchdog.exe""", argv[i + 1]);
    }

    /// <summary>Tách dòng lệnh đúng quy tắc Windows (CommandLineToArgvW) — cách sc.exe nhận tham số.</summary>
    private static string[] CommandLineToArgs(string commandLine)
    {
        IntPtr argv = CommandLineToArgvW(commandLine, out int count);
        try
        {
            var result = new string[count];
            for (int i = 0; i < count; i++)
            {
                result[i] = System.Runtime.InteropServices.Marshal.PtrToStringUni(System.Runtime.InteropServices.Marshal.ReadIntPtr(argv, i * IntPtr.Size))!;
            }

            return result;
        }
        finally
        {
            LocalFree(argv);
        }
    }

    [System.Runtime.InteropServices.DllImport("shell32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CommandLineToArgvW(string lpCmdLine, out int pNumArgs);

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr hMem);
}
