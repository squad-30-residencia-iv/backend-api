using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VoucherCampaigns.Application.Audit;
using VoucherCampaigns.Application.Authentication;
using VoucherCampaigns.Application.Campaigns;
using VoucherCampaigns.Application.DTOs.Organization;
using VoucherCampaigns.Application.Vouchers;
using VoucherCampaigns.Domain.Audit;
using VoucherCampaigns.Domain.Campaigns;
using VoucherCampaigns.Infrastructure.Persistence.Seed;
using VoucherCampaigns.IntegrationTests.Authentication;
using VoucherCampaigns.IntegrationTests.Vouchers;
using static VoucherCampaigns.IntegrationTests.Vouchers.MotorScenario;

namespace VoucherCampaigns.IntegrationTests.Campaigns;

/// <summary>
/// Fatia 1 do endurecimento (spec GH-071): paginação e status efetivo no banco (D-5), trilha que não cabe na coluna
/// (D-6), data de retirada da rota antiga (D-7) e o corte da concessão no fim da vigência (D-3).
/// </summary>
public class HardeningSliceOneEndpointTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly string CampaignsRoute = $"{ReadyCampaign.Route}?networkId={DemoDataSeeder.NetworkId}";
    private static readonly string VouchersRoute = $"/api/v1/vouchers?networkId={DemoDataSeeder.NetworkId}";

    [Fact]
    public async Task CampaignStatusFilterInTheDatabase_MatchesTheEffectiveStatusOfTheUnfilteredList()
    {
        await CampaignsInEveryStatusAsync();
        var user = User(factory);

        var all = (await user.GetFromJsonAsync<List<CampaignResponse>>(CampaignsRoute))!;

        foreach (var status in Enum.GetNames<CampaignStatus>())
        {
            var response = await user.GetAsync($"{CampaignsRoute}&status={status}");
            response.EnsureSuccessStatusCode();
            var filtered = (await response.Content.ReadFromJsonAsync<List<CampaignResponse>>())!;

            Assert.Equal(all.Where(campaign => campaign.Status == status).Select(campaign => campaign.Id), filtered.Select(campaign => campaign.Id));
            Assert.Equal(filtered.Count.ToString(), Assert.Single(response.Headers.GetValues("X-Total-Count")));
        }

        // Cada status que o domínio calcula aparece ao menos uma vez no cenário.
        Assert.Superset(
            new HashSet<string> { "Draft", "PendingApproval", "Rejected", "Scheduled", "Active", "Paused", "Ended", "SoldOut", "Cancelled" },
            all.Select(campaign => campaign.Status).ToHashSet());
    }

    [Fact]
    public async Task CampaignsAndVouchers_ArePagedWhenAsked_WithTheTotalInTheHeader()
    {
        await CampaignsInEveryStatusAsync();
        var user = User(factory);
        var all = (await user.GetFromJsonAsync<List<CampaignResponse>>(CampaignsRoute))!;

        var second = await user.GetAsync($"{CampaignsRoute}&page=2&pageSize=3");
        second.EnsureSuccessStatusCode();
        Assert.Equal(all.Skip(3).Take(3).Select(campaign => campaign.Id), (await second.Content.ReadFromJsonAsync<List<CampaignResponse>>())!.Select(campaign => campaign.Id));
        Assert.Equal(all.Count.ToString(), Assert.Single(second.Headers.GetValues("X-Total-Count")));

        var vouchers = (await user.GetFromJsonAsync<List<PortalVoucherListItemResponse>>(VouchersRoute))!;
        Assert.NotEmpty(vouchers);
        var firstVoucherPage = await user.GetAsync($"{VouchersRoute}&pageSize=1");
        firstVoucherPage.EnsureSuccessStatusCode();
        Assert.Equal(vouchers[0].Id, Assert.Single((await firstVoucherPage.Content.ReadFromJsonAsync<List<PortalVoucherListItemResponse>>())!).Id);
        Assert.Equal(vouchers.Count.ToString(), Assert.Single(firstVoucherPage.Headers.GetValues("X-Total-Count")));

        foreach (var status in new[] { "Provisional", "Available", "Redeemed", "Expired", "Cancelled" })
        {
            var filtered = (await user.GetFromJsonAsync<List<PortalVoucherListItemResponse>>($"{VouchersRoute}&status={status}"))!;
            Assert.Equal(vouchers.Where(voucher => voucher.Status == status).Select(voucher => voucher.Id), filtered.Select(voucher => voucher.Id));
        }

        foreach (var query in new[] { "page=0", "pageSize=0", "pageSize=101" })
        {
            var campaigns = await user.GetAsync($"{CampaignsRoute}&{query}");
            var listed = await user.GetAsync($"{VouchersRoute}&{query}");
            Assert.Equal(HttpStatusCode.BadRequest, campaigns.StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, listed.StatusCode);
        }
    }

    [Fact]
    public async Task LargeScopesAndAudiences_AreAuditedAsASummary_InsteadOfFailing()
    {
        factory.Clock.UtcNow = Setup;
        var admin = User(factory, Roles.Administrador);
        var prefix = Guid.NewGuid().ToString("N")[..6];
        var imported = await admin.PostAsJsonAsync("/api/v1/establishments/import", new
        {
            regionId = DemoDataSeeder.RegionId,
            channels = new[] { "Physical" },
            establishments = Enumerable.Range(1, CampaignContract.MaxScopes).Select(index => new { code = $"{prefix}{index:D3}", name = $"Loja {index}" }).ToArray()
        });
        imported.EnsureSuccessStatusCode();
        var stores = (await imported.Content.ReadFromJsonAsync<ImportEstablishmentsResponse>())!.Establishments;
        var analista = User(factory);
        var id = await ReadyCampaign.CreateAsync(analista);

        (await analista.PutAsJsonAsync($"{ReadyCampaign.Route}/{id}/scopes", new
        {
            scopes = stores.Select(store => new { establishmentId = store.Id, channels = new[] { "Physical" } }).ToArray()
        })).EnsureSuccessStatusCode();

        var cities = Enumerable.Range(0, 100).Select(index => (4100000 + index).ToString()).ToArray();
        var otherCities = Enumerable.Range(0, 100).Select(index => (3500000 + index).ToString()).ToArray();
        foreach (var list in new[] { cities, otherCities })
        {
            (await analista.PutAsJsonAsync($"{ReadyCampaign.Route}/{id}/audience", new
            {
                audienceType = "Restricted",
                criteria = new object[] { new { type = "Location", cityCodes = list } }
            })).EnsureSuccessStatusCode();
        }

        var payloads = await QueryAsync(factory, db => db.AuditEntries
            .Where(entry => entry.CampaignId == id && entry.EventType == AuditEventTypes.CampaignUpdated)
            .OrderBy(entry => entry.Id)
            .Select(entry => entry.Payload!)
            .ToListAsync());
        Assert.All(payloads, payload => Assert.True(payload.Length <= AuditJson.MaxPayloadLength, payload));

        using var scopes = JsonDocument.Parse(payloads.Last(payload => payload.Contains("\"section\":\"scopes\"")));
        var after = scopes.RootElement.GetProperty("after");
        Assert.Equal(CampaignContract.MaxScopes, after.GetProperty("locations").GetInt32());
        Assert.Equal(CampaignContract.MaxScopes, after.GetProperty("establishments").GetInt32());
        Assert.Equal(CampaignContract.MaxScopes, after.GetProperty("changes").GetProperty("added").GetInt32());

        var audiencePayload = payloads.Last(payload => payload.Contains("\"section\":\"audience\""));
        Assert.Contains("cityCodesCount", audiencePayload);
        using var audience = JsonDocument.Parse(audiencePayload);
        var criterion = audience.RootElement.GetProperty("after").GetProperty("criteria")[0];
        Assert.Equal(100, criterion.GetProperty("cityCodesCount").GetInt32());
        var locationChanges = audience.RootElement.GetProperty("after").GetProperty("locationChanges");
        Assert.Equal(100, locationChanges.GetProperty("cityCodesAdded").GetInt32());
        Assert.Equal(100, locationChanges.GetProperty("cityCodesRemoved").GetInt32());
    }

    [Fact]
    public async Task LegacyVoucherLookup_SendsSunsetOnlyWhenConfigured()
    {
        var withoutSunset = await Satellite(factory).GetAsync($"/api/v1/satellite/customers/{DocumentDigits}/vouchers");
        withoutSunset.EnsureSuccessStatusCode();
        Assert.False(withoutSunset.Headers.Contains("Sunset"));
        Assert.Equal("true", Assert.Single(withoutSunset.Headers.GetValues("Deprecation")));

        var connectionString = ApiFactory.NewSqliteConnectionString();
        await using var configured = ApiFactory.For("Sqlite", connectionString, settings: new Dictionary<string, string?>
        {
            ["Satellite:LegacyVoucherLookupSunset"] = "2027-03-31"
        });

        var withSunset = await Satellite(configured).GetAsync($"/api/v1/satellite/customers/{DocumentDigits}/vouchers");
        withSunset.EnsureSuccessStatusCode();
        Assert.Equal("Wed, 31 Mar 2027 00:00:00 GMT", Assert.Single(withSunset.Headers.GetValues("Sunset")));

        await configured.DisposeAsync();
        ApiFactory.DeleteSqliteDatabase(connectionString);
    }

    [Fact]
    public async Task Concession_UntilTheLastSecondOfTheEndMinute_AndNotAfter()
    {
        // D-3: o corte no banco não pode tirar a campanha antes do fim do último minuto (D9).
        var scenario = await ApprovedCampaignAsync(factory);
        var satellite = Satellite(factory);

        factory.Clock.UtcNow = ReadyCampaign.EndsAt.AddSeconds(59);
        await GrantedAsync(satellite, Purchase(scenario.StoreId, document: TestCpf.New()));

        factory.Clock.UtcNow = ReadyCampaign.EndsAt.AddMinutes(1);
        await NotGrantedAsync(satellite, Purchase(scenario.StoreId, document: TestCpf.New()), "NoCampaignMatched");
        factory.Clock.UtcNow = Now;
    }

    /// <summary>Campanhas da rede de demonstração em todos os status efetivos, com vouchers em vários status.</summary>
    private async Task CampaignsInEveryStatusAsync()
    {
        factory.Clock.UtcNow = Setup;
        var analista = User(factory);
        var gestor = User(factory, Roles.Gestor);

        await ReadyCampaign.CreateAsync(analista);
        await ReadyCampaign.CreateSubmittedAsync(analista);
        var rejected = await ReadyCampaign.CreateSubmittedAsync(analista);
        (await gestor.PostAsync($"{ReadyCampaign.Route}/{rejected}/reject", null)).EnsureSuccessStatusCode();

        var active = await ApprovedCampaignAsync(factory);
        var paused = await ApprovedCampaignAsync(factory);
        var ended = await ApprovedCampaignAsync(factory);
        var cancelled = await ApprovedCampaignAsync(factory);
        var soldOut = await ApprovedCampaignAsync(factory, (client, id) =>
            client.PutAsJsonAsync($"{ReadyCampaign.Route}/{id}/quantities", new { totalVoucherLimit = 1 }));

        // Agendada: aprovada para começar depois do "agora" do teste (10/10).
        factory.Clock.UtcNow = Setup;
        var scheduled = await ReadyCampaign.CreateAsync(analista);
        (await analista.PutAsJsonAsync($"{ReadyCampaign.Route}/{scheduled}", new
        {
            name = "Agendada para novembro",
            startsAt = "2026-11-02T08:00:00",
            endsAt = "2026-11-30T23:59:00"
        })).EnsureSuccessStatusCode();
        (await analista.PutAsJsonAsync($"{ReadyCampaign.Route}/{scheduled}/scopes", new { scopes = new[] { new { channels = new[] { "Physical" } } } }))
            .EnsureSuccessStatusCode();
        (await analista.PostAsync($"{ReadyCampaign.Route}/{scheduled}/submit", null)).EnsureSuccessStatusCode();
        (await gestor.PostAsync($"{ReadyCampaign.Route}/{scheduled}/approve", null)).EnsureSuccessStatusCode();
        factory.Clock.UtcNow = Now;

        // Vouchers: resgatado, provisório e disponíveis.
        var satellite = Satellite(factory);
        var customer = TestCpf.New();
        var voucher = await AvailableVoucherAsync(satellite, active, customer);
        await RedeemedAsync(satellite, Redeem(voucher.Id, active.StoreId, document: customer));
        await GrantedAsync(satellite, Purchase(active.StoreId, document: TestCpf.New()));
        await AvailableVoucherAsync(satellite, soldOut, TestCpf.New());
        await AvailableVoucherAsync(satellite, cancelled, TestCpf.New());

        (await analista.PostAsync($"{ReadyCampaign.Route}/{paused.CampaignId}/pause", null)).EnsureSuccessStatusCode();
        (await analista.PostAsync($"{ReadyCampaign.Route}/{ended.CampaignId}/end", null)).EnsureSuccessStatusCode();
        (await User(factory, Roles.Gestor).PostAsJsonAsync($"{ReadyCampaign.Route}/{cancelled.CampaignId}/cancel", new { reason = "Teste" }))
            .EnsureSuccessStatusCode();
    }
}
