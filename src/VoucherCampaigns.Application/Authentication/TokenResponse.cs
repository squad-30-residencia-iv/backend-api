namespace VoucherCampaigns.Application.Authentication;

public record TokenResponse(string Token, int ExpiresIn, string Message);
