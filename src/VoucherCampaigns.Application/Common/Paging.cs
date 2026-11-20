namespace VoucherCampaigns.Application.Common;

/// <summary>
/// Página opcional das listagens (spec GH-071, D-5; PERF-03 da auditoria). Sem <c>page</c> e sem <c>pageSize</c>, a
/// listagem traz tudo, como antes; com qualquer um deles, pagina (página 1 e 20 itens quando o outro falta).
/// </summary>
public readonly record struct PageRequest(int Skip, int Take)
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;
    public const string PageInvalid = "A página começa em 1.";
    public const string PageSizeInvalid = "O tamanho da página vai de 1 a 100.";

    /// <summary>Null quando a chamada não pediu página.</summary>
    public static PageRequest? Parse(int? page, int? pageSize)
    {
        if (page is null && pageSize is null)
        {
            return null;
        }

        if (page is < 1)
        {
            throw new RequestValidationException("page", PageInvalid);
        }

        if (pageSize is < 1 or > MaxPageSize)
        {
            throw new RequestValidationException("pageSize", PageSizeInvalid);
        }

        var size = pageSize ?? DefaultPageSize;

        return new PageRequest(((page ?? 1) - 1) * size, size);
    }
}

/// <summary>Itens da página e o total da consulta inteira (vai no cabeçalho <c>X-Total-Count</c>).</summary>
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Total);
