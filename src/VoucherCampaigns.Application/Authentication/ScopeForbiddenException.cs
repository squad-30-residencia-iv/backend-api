namespace VoucherCampaigns.Application.Authentication;

/// <summary>Operação sobre uma rede diferente da rede do token; a Api responde 403.</summary>
public sealed class ScopeForbiddenException() : Exception("Escopo organizacional fora da sua permissão de acesso");
