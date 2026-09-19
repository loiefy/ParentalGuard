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
        Assert.Contains(@"binPath= ""C:\Program Files\ParentalGuard\ParentalGuard.Watchdog.exe""", args);
        Assert.Contains("start= auto", args); // SERVICE_AUTO_START, không Delayed (02 mục 2.1)
        Assert.Contains("obj= LocalSystem", args);
        Assert.Contains(@"DisplayName= ""ParentalGuard Watchdog""", args);
    }
}
