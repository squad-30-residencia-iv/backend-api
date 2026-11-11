namespace VoucherCampaigns.Application.Authentication;

public interface ISatelliteTokenRateLimiter
{
    bool IsLockedOut(string clientId);
    void RecordFailure(string clientId);
    void Reset(string clientId);
}
