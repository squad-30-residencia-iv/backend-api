namespace VoucherCampaigns.Application.Authentication;

public record UserProfileResponse(Guid UserId, string Email, string Role, Guid? NetworkId);

public record TokenResponse(
    string Token,
    int ExpiresIn,
    string Message,
    UserProfileResponse? Profile = null);

