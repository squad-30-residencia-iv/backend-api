using System.Net.Http.Json;
using VoucherCampaigns.Application.Campaigns;
using VoucherCampaigns.Infrastructure.Persistence.Seed;

namespace VoucherCampaigns.IntegrationTests.Campaigns;

/// <summary>Rascunho com tudo o que a submissão exige (US-01.6), vigência de 01/10/2026 08:00 a 31/10/2026 23:59 em Brasília.</summary>
internal static class ReadyCampaign
{
    public const string Route = "/api/v1/campaigns";

    /// <summary>Início da vigência em UTC (08:00 em Brasília).</summary>
    public static readonly DateTimeOffset StartsAt = new(2026, 10, 1, 8, 0, 0, TimeSpan.FromHours(-3));

    /// <summary>Fim da vigência em UTC (23:59 em Brasília).</summary>
    public static readonly DateTimeOffset EndsAt = new(2026, 10, 31, 23, 59, 0, TimeSpan.FromHours(-3));

    public static async Task<Guid> CreateAsync(HttpClient analista)
    {
        var created = await analista.PostAsJsonAsync(Route, new
        {
            networkId = DemoDataSeeder.NetworkId,
            name = $"Campanha {Guid.NewGuid():N}",
            startsAt = "2026-10-01T08:00:00",
            endsAt = "2026-10-31T23:59:00",
        });
        created.EnsureSuccessStatusCode();
        var id = (await created.Content.ReadFromJsonAsync<CampaignResponse>())!.Id;

        (await analista.PutAsJsonAsync($"{Route}/{id}/items", new { items = new[] { new { targetType = "All", benefitType = "Percentage", benefitValue = 10 } } })).EnsureSuccessStatusCode();
        (await analista.PutAsJsonAsync($"{Route}/{id}/quantities", new { totalVoucherLimit = 100 })).EnsureSuccessStatusCode();
        (await analista.PutAsJsonAsync($"{Route}/{id}/audience", new { audienceType = "AllCustomers" })).EnsureSuccessStatusCode();
        (await analista.PutAsJsonAsync($"{Route}/{id}/voucher-validity", new { type = "Relative", amount = 30, unit = "Days" })).EnsureSuccessStatusCode();
        (await analista.PutAsJsonAsync($"{Route}/{id}/usage-restrictions", new { maxUsesPerVoucher = 1 })).EnsureSuccessStatusCode();

        return id;
    }

    public static async Task<Guid> CreateSubmittedAsync(HttpClient analista)
    {
        var id = await CreateAsync(analista);
        (await analista.PostAsync($"{Route}/{id}/submit", null)).EnsureSuccessStatusCode();

        return id;
    }
}
