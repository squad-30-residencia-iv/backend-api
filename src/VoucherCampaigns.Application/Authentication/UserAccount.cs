namespace VoucherCampaigns.Application.Authentication;

public record UserAccount(Guid UserId, string Email, string Role, Guid NetworkId);
