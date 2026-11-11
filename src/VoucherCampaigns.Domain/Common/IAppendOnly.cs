namespace VoucherCampaigns.Domain.Common;

/// <summary>Registro de histórico: só aceita inclusão. O <c>AppDbContext</c> recusa alterar ou apagar.</summary>
public interface IAppendOnly;
