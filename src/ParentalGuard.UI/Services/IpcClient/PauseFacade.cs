using ParentalGuard.Ipc.Client;

namespace ParentalGuard.UI.Services.IpcClient;

/// <summary>Chưa implement — placeholder giữ chỗ DI cho giai đoạn viết `S2` Dashboard.</summary>
public sealed class PauseFacade(UiIpcClient client) : IPauseFacade
{
    private readonly UiIpcClient _client = client;
}
