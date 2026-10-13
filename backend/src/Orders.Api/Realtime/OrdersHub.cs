using Microsoft.AspNetCore.SignalR;

namespace Orders.Api.Realtime;

/// <summary>
/// Hub de tempo real, somente servidor -> cliente. Toda conexao entra no grupo "orders" e recebe
/// o evento OrderStatusChanged; a lista e a pagina de detalhes filtram pelo pedido que exibem.
/// </summary>
public sealed class OrdersHub : Hub
{
    public const string AllOrdersGroup = "orders";

    public override async Task OnConnectedAsync()
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, AllOrdersGroup);
        await base.OnConnectedAsync();
    }
}

/// <summary>Payload enviado aos navegadores a cada mudanca de status.</summary>
public sealed record OrderStatusChangedNotification(
    Guid OrderId,
    string? StatusAnterior,
    string StatusNovo,
    DateTimeOffset AlteradoEm);
