namespace VoucherCampaigns.Application.Authentication;

internal static class NetworkScope
{
    public static void Ensure(ICurrentUser currentUser, Guid networkId)
    {
        if (networkId != currentUser.NetworkId)
        {
            throw new ScopeForbiddenException();
        }
    }
}
