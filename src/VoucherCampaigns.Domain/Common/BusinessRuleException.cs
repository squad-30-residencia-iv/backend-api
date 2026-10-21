namespace VoucherCampaigns.Domain.Common;

/// <summary>Violação de regra de negócio; a Api responde 422 com <see cref="Code"/> e a mensagem.</summary>
public class BusinessRuleException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
