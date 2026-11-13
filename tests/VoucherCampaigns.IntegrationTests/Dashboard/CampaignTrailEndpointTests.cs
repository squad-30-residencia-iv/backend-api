using System.Net.Http.Json;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using VoucherCampaigns.Application.Authentication;
using VoucherCampaigns.Application.Campaigns;
using VoucherCampaigns.Application.Dashboard;
using VoucherCampaigns.Domain.Audit;
using VoucherCampaigns.Domain.Campaigns;
using VoucherCampaigns.Infrastructure.Persistence;
using VoucherCampaigns.Infrastructure.Persistence.Seed;
using VoucherCampaigns.IntegrationTests.Authentication;
using VoucherCampaigns.IntegrationTests.Campaigns;

namespace VoucherCampaigns.IntegrationTests.Dashboard;

/// <summary>Trilha de auditoria da campanha (US-03.4) e auditoria da criação e das edições (spec GH-050, D-8 e D-9).</summary>
public class CampaignTrailEndpointTests : IClassFixture<ApiFactory>
{
    private static readonly DateTimeOffset Now = new(2026, 9, 28, 12, 0, 0, TimeSpan.FromHours(-3));
    private static readonly Guid AnalistaId = Guid.Parse("0199e3a0-0000-7000-8000-000000000901");
    private static readonly Guid GestorId = Guid.Parse("0199e3a0-0000-7000-8000-000000000902");

    private readonly ApiFactory _factory;

    public CampaignTrailEndpointTests(ApiFactory factory)
    {
        _factory = factory;
        _factory.Clock.UtcNow = Now;
    }

    [Fact]
    public async Task Trail_ListsCreationEditsAndLifecycle_InOrderWithTheResponsible()
    {
        var analista = Analista();
        var id = await ReadyCampaign.CreateAsync(analista);
        _factory.Clock.UtcNow = Now.AddMinutes(10);
        var sameItems = new { items = new[] { new { targetType = "All", benefitType = "Percentage", benefitValue = 10 } } };
        (await analista.PutAsJsonAsync($"{ReadyCampaign.Route}/{id}/items", sameItems)).EnsureSuccessStatusCode();
        (await analista.PutAsJsonAsync($"{ReadyCampaign.Route}/{id}/items", new { items = new[] { new { targetType = "All", benefitType = "FixedAmount", benefitValue = 15 } } })).EnsureSuccessStatusCode();
        (await analista.PostAsync($"{ReadyCampaign.Route}/{id}/submit", null)).EnsureSuccessStatusCode();
        _factory.Clock.UtcNow = Now.AddMinutes(20);
        (await Gestor().PostAsync($"{ReadyCampaign.Route}/{id}/approve", null)).EnsureSuccessStatusCode();

        var trail = await analista.GetFromJsonAsync<CampaignAuditResponse>($"{ReadyCampaign.Route}/{id}/audit");

        Assert.Equal(id, trail!.CampaignId);
        Assert.Equal("Scheduled", trail.Status);
        var events = trail.Entries.Select(entry => entry.EventType).ToList();
        Assert.Equal(AuditEventTypes.CampaignCreated, events[0]);
        Assert.Equal([AuditEventTypes.CampaignSubmitted, AuditEventTypes.CampaignApproved], events.TakeLast(2));
        Assert.Equal(trail.Entries.OrderBy(entry => entry.OccurredAt).Select(entry => entry.Id), trail.Entries.Select(entry => entry.Id));
        Assert.All(trail.Entries, entry => Assert.False(entry.Synthetic));

        var created = trail.Entries[0];
        Assert.Equal(Now, created.OccurredAt);
        Assert.Equal(new AuditActorResponse("User", AnalistaId, "Ana Lima", Roles.Analista), created.Actor);
        Assert.Equal(DemoDataSeeder.NetworkId.ToString(), created.Payload!.Value.GetProperty("networkId").GetString());

        // ReadyCampaign grava uma edição por seção; a repetição idêntica dos itens não gera evento, a troca gera com antes e depois.
        var itemEdits = trail.Entries.Where(entry => entry.EventType == AuditEventTypes.CampaignUpdated && entry.Payload!.Value.GetProperty("section").GetString() == "items").ToList();
        Assert.Equal(2, itemEdits.Count);
        var changed = itemEdits[1].Payload!.Value;
        Assert.Equal("Percentage", changed.GetProperty("before")[0].GetProperty("benefitType").GetString());
        Assert.Equal("FixedAmount", changed.GetProperty("after")[0].GetProperty("benefitType").GetString());
        Assert.Contains(trail.Entries, entry => entry.EventType == AuditEventTypes.CampaignUpdated && entry.Payload!.Value.GetProperty("section").GetString() == "scopes");

        var approved = trail.Entries[^1];
        Assert.Equal(new AuditActorResponse("User", GestorId, "Bruno Costa", Roles.Gestor), approved.Actor);
        Assert.Equal(Now.AddMinutes(20), approved.OccurredAt);
    }

    [Fact]
    public async Task Trail_OfACampaignCreatedBeforeTheAudit_StartsWithASyntheticCreation()
    {
        Guid id;
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var campaign = Campaign.CreateDraft(DemoDataSeeder.NetworkId, "Campanha antiga", null, Now.AddDays(3).UtcDateTime, Now.AddDays(30).UtcDateTime, Now.UtcDateTime);
            db.Campaigns.Add(campaign);
            await db.SaveChangesAsync();
            id = campaign.Id;
        }

        var trail = await Analista().GetFromJsonAsync<CampaignAuditResponse>($"{ReadyCampaign.Route}/{id}/audit");

        var only = Assert.Single(trail!.Entries);
        Assert.Equal(AuditEventTypes.CampaignCreated, only.EventType);
        Assert.True(only.Synthetic);
        Assert.Null(only.Actor);
        Assert.Equal(Now, only.OccurredAt);
    }

    [Fact]
    public async Task Trail_DoesNotListVoucherEvents_AndExportsAsCsv()
    {
        var analista = Analista();
        var id = await ReadyCampaign.CreateSubmittedAsync(analista);
        (await Gestor().PostAsync($"{ReadyCampaign.Route}/{id}/approve", null)).EnsureSuccessStatusCode();

        var trail = await analista.GetFromJsonAsync<CampaignAuditResponse>($"{ReadyCampaign.Route}/{id}/audit");
        Assert.DoesNotContain(trail!.Entries, entry => entry.EventType.StartsWith("Voucher", StringComparison.Ordinal));

        var response = await analista.GetAsync($"{ReadyCampaign.Route}/{id}/audit/export");
        var text = Encoding.UTF8.GetString(await response.Content.ReadAsByteArrayAsync());

        response.EnsureSuccessStatusCode();
        Assert.Equal("text/csv", response.Content.Headers.ContentType!.MediaType);
        Assert.Contains("Data/hora;Evento;Responsável;Papel;Origem;Detalhes", text);
        Assert.Contains("CampaignCreated;Ana Lima;analista;User;", text);
        Assert.Contains("CampaignApproved;Bruno Costa;gestor;User;", text);
    }

    [Fact]
    public async Task Edits_OfEverySection_AreAuditedOnlyWhenSomethingChanges()
    {
        var analista = Analista();
        var id = await ReadyCampaign.CreateAsync(analista);
        var before = (await analista.GetFromJsonAsync<CampaignAuditResponse>($"{ReadyCampaign.Route}/{id}/audit"))!.Entries.Count;

        // Repete a configuração que a ReadyCampaign já gravou: nada muda, nada é auditado.
        (await analista.PutAsJsonAsync($"{ReadyCampaign.Route}/{id}/quantities", new { totalVoucherLimit = 100 })).EnsureSuccessStatusCode();
        (await analista.PutAsJsonAsync($"{ReadyCampaign.Route}/{id}/audience", new { audienceType = "AllCustomers" })).EnsureSuccessStatusCode();
        (await analista.PutAsJsonAsync($"{ReadyCampaign.Route}/{id}/voucher-validity", new { type = "Relative", amount = 30, unit = "Days" })).EnsureSuccessStatusCode();
        (await analista.PutAsJsonAsync($"{ReadyCampaign.Route}/{id}/usage-restrictions", new { maxUsesPerVoucher = 1 })).EnsureSuccessStatusCode();
        var unchanged = (await analista.GetFromJsonAsync<CampaignAuditResponse>($"{ReadyCampaign.Route}/{id}/audit"))!.Entries.Count;
        Assert.Equal(before, unchanged);

        (await analista.PutAsJsonAsync($"{ReadyCampaign.Route}/{id}", new { name = "Nome novo", startsAt = "2026-10-01T08:00:00", endsAt = "2026-10-31T23:59:00" })).EnsureSuccessStatusCode();
        (await analista.PutAsJsonAsync($"{ReadyCampaign.Route}/{id}/conditions", new { conditions = new[] { new { type = "PurchaseTotal", minAmount = 50 } }, allowUseInSamePurchase = true })).EnsureSuccessStatusCode();
        (await analista.PutAsJsonAsync($"{ReadyCampaign.Route}/{id}/usage-restrictions", new { minPurchaseAmount = 10, maxUsesPerVoucher = 2 })).EnsureSuccessStatusCode();

        var entries = (await analista.GetFromJsonAsync<CampaignAuditResponse>($"{ReadyCampaign.Route}/{id}/audit"))!.Entries;
        var sections = entries.Skip(unchanged).Select(entry => entry.Payload!.Value.GetProperty("section").GetString()).ToList();
        Assert.Equal(["details", "conditions", "usageRestrictions"], sections);
        var details = entries[unchanged].Payload!.Value;
        Assert.Equal("Nome novo", details.GetProperty("after").GetProperty("name").GetString());
        Assert.NotEqual("Nome novo", details.GetProperty("before").GetProperty("name").GetString());
    }

    private HttpClient Analista() =>
        _factory.CreateClient().WithBearer(TestAuth.CreateToken(_factory, Roles.Analista, userId: AnalistaId));

    private HttpClient Gestor() =>
        _factory.CreateClient().WithBearer(TestAuth.CreateToken(_factory, Roles.Gestor, userId: GestorId));
}
