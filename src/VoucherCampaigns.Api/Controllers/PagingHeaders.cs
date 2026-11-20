namespace VoucherCampaigns.Api.Controllers;

/// <summary>Cabeçalhos da paginação opcional das listagens (spec GH-071, D-5).</summary>
public static class PagingHeaders
{
    /// <summary>Total de itens da consulta filtrada inteira, com ou sem página.</summary>
    public const string TotalCount = "X-Total-Count";
}
