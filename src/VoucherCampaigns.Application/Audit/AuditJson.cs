using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Unicode;

namespace VoucherCampaigns.Application.Audit;

/// <summary>Payload resumido da auditoria e do histórico: camelCase, sem nulos e com acentos legíveis no banco (GH-026, D-3).</summary>
public static class AuditJson
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All)
    };

    public static string Serialize<T>(T payload) => JsonSerializer.Serialize(payload, Options);
}
