using System.Text.Json;
using System.Text.Json.Serialization;

namespace Orders.Contracts;

/// <summary>Serializacao unica para mensagens: snake_case, igual a API.</summary>
public static class MessagingJson
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        Converters = { new JsonStringEnumConverter() },
    };
}
