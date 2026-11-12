namespace VoucherCampaigns.Domain.Common;

/// <summary>Violação de regra de negócio; a Api responde 422 com <see cref="Code"/>, a mensagem e as <see cref="Extensions"/>.</summary>
public class BusinessRuleException(string code, string message, IReadOnlyDictionary<string, object?>? extensions = null) : Exception(message)
{
    public string Code { get; } = code;

    /// <summary>Campos extras do problem details (ex.: <c>reason</c> do resgate, spec GH-049 D-6).</summary>
    public IReadOnlyDictionary<string, object?> Extensions { get; } = extensions ?? new Dictionary<string, object?>();
}
