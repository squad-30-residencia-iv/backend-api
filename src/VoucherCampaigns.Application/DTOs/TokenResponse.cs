namespace VoucherCampaigns.Application.DTOs;

public record TokenResponse(string Token, int ExpiresIn, string Message);