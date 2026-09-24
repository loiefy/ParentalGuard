using ParentalGuard.Ipc.Client;

namespace ParentalGuard.UI.Services.IpcClient;

/// <summary>Chưa implement — placeholder giữ chỗ DI cho giai đoạn viết `S4` Cài đặt.</summary>
public sealed class ConfigFacade(UiIpcClient client) : IConfigFacade
{
    private readonly UiIpcClient _client = client;
}
