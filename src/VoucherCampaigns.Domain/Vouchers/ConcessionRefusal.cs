namespace VoucherCampaigns.Domain.Vouchers;

/// <summary>Motivo da não concessão (US-04.7, US-07.2 cenário 2). Gravado como texto.</summary>
public enum ConcessionRefusal
{
    /// <summary>Nenhuma campanha do escopo teve as condições satisfeitas pela compra.</summary>
    NoCampaignMatched,

    /// <summary>A campanha satisfeita não está efetivamente ativa (agendada, pausada, encerrada, cancelada).</summary>
    CampaignNotActive,

    /// <summary>A campanha satisfeita não tem mais saldo (RN09); as reservas contam.</summary>
    CampaignSoldOut,

    /// <summary>O alvo que originaria o voucher não tem mais cota (RN09a).</summary>
    TargetSoldOut,

    /// <summary>O cliente já atingiu o limite de vouchers desta campanha (RF06c).</summary>
    CustomerLimitReached,

    /// <summary>O cliente não atende ao público restrito da campanha (RN11, US-04.2 cenário 2, spec GH-047).</summary>
    CustomerNotEligible
}
