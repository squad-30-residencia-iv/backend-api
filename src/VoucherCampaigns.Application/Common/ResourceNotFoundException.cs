namespace VoucherCampaigns.Application.Common;

/// <summary>Recurso ausente; a Api responde 404 com <c>NOT_FOUND</c>.</summary>
public sealed class ResourceNotFoundException(string message) : Exception(message);
