using ParentalGuard.Ipc.Client;

namespace ParentalGuard.UI.Services.IpcClient;

/// <summary>Chưa implement — placeholder giữ chỗ DI cho giai đoạn viết `S3` Audit log.</summary>
public sealed class AuditFacade(UiIpcClient client) : IAuditFacade
{
    private readonly UiIpcClient _client = client;
}
