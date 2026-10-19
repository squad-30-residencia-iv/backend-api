namespace VoucherCampaigns.Application.Common;

/// <summary>Erro de contrato só detectável no serviço (ex.: depende de dado do banco); a Api responde 400.</summary>
public class RequestValidationException(string field, string message) : Exception(message)
{
    public string Field { get; } = field;
}
