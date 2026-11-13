using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VoucherCampaigns.Application.Authentication;
using VoucherCampaigns.Application.Campaigns;
using VoucherCampaigns.Application.Dashboard;
using VoucherCampaigns.Application.Vouchers;
using VoucherCampaigns.Domain.Audit;
using VoucherCampaigns.Domain.Campaigns;
using VoucherCampaigns.Domain.Vouchers;
using VoucherCampaigns.IntegrationTests.Authentication;
using VoucherCampaigns.IntegrationTests.Vouchers;
using static VoucherCampaigns.IntegrationTests.Vouchers.MotorScenario;

namespace VoucherCampaigns.IntegrationTests.Campaigns;

/// <summary>Cancelamento da campanha com invalidação dos vouchers não utilizados (US-01.8, RN10, US-05.5; spec GH-061).</summary>
public class CampaignCancellationEndpointTests : IClassFixture<ApiFactory>
{
    private const string Reason = "Erro de configuração no desconto";

    private readonly ApiFactory _factory;

    public CampaignCancellationEndpointTests(ApiFactory factory)
    {
        _factory = factory;
        _factory.Clock.UtcNow = Now;
    }

    // ---------- US-01.8 cenários 3 e 5, US-05.5 cenários 2 e 3 ----------

    [Fact]
    public async Task Cancel_InvalidatesTheUnusedVouchers_AndKeepsTheRedeemedOnes()
    {
        var scenario = await ApprovedCampaignAsync(_factory);
        var satellite = Satellite(_factory);
        var used = await AvailableVoucherAsync(satellite, scenario, TestCpf.New());
        var first = await AvailableVoucherAsync(satellite, scenario, TestCpf.New());
        var second = await AvailableVoucherAsync(satellite, scenario, TestCpf.New());
        var redemption = await RedeemedAsync(satellite, Redeem(used.Id, scenario.StoreId, document: await DocumentOfAsync(used.Id)));

        var response = await CancelAsync(User(_factory), scenario.CampaignId, $"  {Reason}  ");

        response.EnsureSuccessStatusCode();
        Assert.Equal("Cancelled", (await response.Content.ReadFromJsonAsync<CampaignResponse>())!.Status);
        Assert.Equal(VoucherStatus.Redeemed, await StatusOfAsync(used.Id));
        Assert.Equal(VoucherStatus.Cancelled, await StatusOfAsync(first.Id));
        Assert.Equal(VoucherStatus.Cancelled, await StatusOfAsync(second.Id));
        Assert.NotNull(await QueryAsync(_factory, db => db.Vouchers.Where(voucher => voucher.Id == first.Id).Select(voucher => voucher.CancelledAt).SingleAsync()));

        // O resgate anterior fica intacto e o voucher utilizado não ganha evento (US-05.5 cenário 3).
        var stored = await satellite.GetFromJsonAsync<RedemptionDto>($"{RedemptionsRoute}/{redemption.RedemptionId}");
        Assert.Equal("Effective", stored!.Status);
        Assert.DoesNotContain(VoucherEventTypes.Cancelled, await EventsOfAsync(used.Id));

        var dashboard = await User(_factory).GetFromJsonAsync<CampaignDashboardResponse>($"{ReadyCampaign.Route}/{scenario.CampaignId}/dashboard");
        var indicators = dashboard!.Indicators;
        Assert.Equal("Cancelled", dashboard.Status);
        Assert.Equal((3, 1, 2, 0), (indicators.Issued, indicators.Redeemed, indicators.CancelledUnused, indicators.Available));
        Assert.Equal(indicators.Issued, indicators.Redeemed + indicators.ExpiredUnused + indicators.CancelledUnused + indicators.Available);
    }

    [Fact]
    public async Task EndAndCancel_AreDistinct_EndKeepsTheVouchersAndCancelInvalidatesThem()
    {
        var ended = await ApprovedCampaignAsync(_factory);
        var cancelled = await ApprovedCampaignAsync(_factory);
        var satellite = Satellite(_factory);
        var kept = await AvailableVoucherAsync(satellite, ended, TestCpf.New());
        var lost = await AvailableVoucherAsync(satellite, cancelled, TestCpf.New());

        (await User(_factory).PostAsync($"{ReadyCampaign.Route}/{ended.CampaignId}/end", null)).EnsureSuccessStatusCode();
        (await CancelAsync(User(_factory), cancelled.CampaignId)).EnsureSuccessStatusCode();

        Assert.Equal(VoucherStatus.Available, await StatusOfAsync(kept.Id));
        Assert.Equal(VoucherStatus.Cancelled, await StatusOfAsync(lost.Id));
    }

    [Fact]
    public async Task AfterCancel_TheSatelliteNoLongerIssuesNorRedeems()
    {
        var scenario = await ApprovedCampaignAsync(_factory);
        var satellite = Satellite(_factory);
        var document = TestCpf.New();
        var voucher = await AvailableVoucherAsync(satellite, scenario, document);

        (await CancelAsync(User(_factory), scenario.CampaignId)).EnsureSuccessStatusCode();

        var check = await CheckAsync(satellite, Purchase(scenario.StoreId, document: document));
        Assert.Equal("Cancelled", Assert.Single(check.NotApplicable, item => item.Voucher.Id == voucher.Id).Reason);
        var (redeem, _) = await RedeemAsync(satellite, Redeem(voucher.Id, scenario.StoreId, document: document));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, redeem.StatusCode);
        Assert.Equal("REDEMPTION_CANCELLED", (await redeem.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        await NotGrantedAsync(satellite, Purchase(scenario.StoreId, document: TestCpf.New()), "CampaignNotActive");
        var listed = await satellite.GetFromJsonAsync<JsonElement>("/api/v1/satellite/campaigns");
        Assert.DoesNotContain(scenario.CampaignId.ToString(), listed.GetRawText());
    }

    // ---------- D-5: o que é "não utilizado" ----------

    [Fact]
    public async Task Cancel_PartiallyUsedVoucherLosesTheRest_AndAnExpiredOneIsLeftAsIs()
    {
        var scenario = await ApprovedCampaignAsync(_factory, async (analista, id) =>
        {
            (await analista.PutAsJsonAsync($"{ReadyCampaign.Route}/{id}/usage-restrictions", new { maxUsesPerVoucher = 2 })).EnsureSuccessStatusCode();
            (await analista.PutAsJsonAsync($"{ReadyCampaign.Route}/{id}/voucher-validity", new { type = "Relative", amount = 2, unit = "Days" })).EnsureSuccessStatusCode();
        });
        var satellite = Satellite(_factory);
        var expiring = await AvailableVoucherAsync(satellite, scenario, TestCpf.New());
        _factory.Clock.UtcNow = Now.AddDays(3);
        var partialDocument = TestCpf.New();
        var partial = await AvailableVoucherAsync(satellite, scenario, partialDocument);
        await RedeemedAsync(satellite, Redeem(partial.Id, scenario.StoreId, document: partialDocument));

        (await CancelAsync(User(_factory), scenario.CampaignId)).EnsureSuccessStatusCode();

        var stored = await QueryAsync(_factory, db => db.Vouchers.AsNoTracking().SingleAsync(voucher => voucher.Id == partial.Id));
        Assert.Equal((VoucherStatus.Cancelled, 1), (stored.Status, stored.UsesCount));
        Assert.Equal(VoucherStatus.Available, await StatusOfAsync(expiring.Id));
        Assert.Equal(VoucherStatus.Expired, (await QueryAsync(_factory, db => db.Vouchers.AsNoTracking().SingleAsync(voucher => voucher.Id == expiring.Id)))
            .EffectiveStatus(_factory.Clock.GetUtcNow().UtcDateTime));

        var indicators = (await User(_factory).GetFromJsonAsync<CampaignDashboardResponse>($"{ReadyCampaign.Route}/{scenario.CampaignId}/dashboard"))!.Indicators;
        Assert.Equal((2, 1, 1, 0, 0), (indicators.Issued, indicators.Redeemed, indicators.ExpiredUnused, indicators.CancelledUnused, indicators.Available));
    }

    // ---------- D-6 e D-11: provisório ----------

    [Fact]
    public async Task Cancel_DiscardsTheLiveProvisional_ReversesTheSamePurchaseRedemption_AndTheConfirmationIsRefused()
    {
        var scenario = await ApprovedCampaignAsync(_factory, (analista, id) =>
            analista.PutAsJsonAsync($"{ReadyCampaign.Route}/{id}/conditions", new { conditions = Array.Empty<object>(), allowUseInSamePurchase = true }));
        var satellite = Satellite(_factory);
        var granted = await GrantedAsync(satellite, Purchase(scenario.StoreId, "TRX-061-PROV"));
        var redeemed = await RedeemedAsync(satellite, Redeem(granted.Voucher!.Id, scenario.StoreId, "TRX-061-PROV"));

        (await CancelAsync(User(_factory), scenario.CampaignId)).EnsureSuccessStatusCode();

        Assert.Equal(VoucherStatus.Discarded, await StatusOfAsync(granted.Voucher.Id));
        var campaign = await StoredCampaignAsync(_factory, scenario.CampaignId);
        Assert.Equal((0, 0), (campaign.IssuedVouchers, campaign.ReservedVouchers));
        Assert.Equal(0, campaign.Items.Single().ReservedVouchers);
        var redemption = await satellite.GetFromJsonAsync<RedemptionDto>($"{RedemptionsRoute}/{redeemed.RedemptionId}");
        Assert.Equal("Reversed", redemption!.Status);

        var confirm = await satellite.PostAsync($"{ConcessionsRoute}/{granted.ConcessionId}/confirm", null);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, confirm.StatusCode);
        Assert.Equal("CONCESSION_CAMPAIGN_CANCELLED", (await confirm.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());

        var undo = await satellite.PostAsync($"{ConcessionsRoute}/{granted.ConcessionId}/cancel", null);
        undo.EnsureSuccessStatusCode();
        Assert.Equal("Cancelled", (await undo.Content.ReadFromJsonAsync<ConcessionResponseDto>())!.Status);

        var audit = await CancellationAuditAsync(scenario.CampaignId);
        Assert.Equal(0, audit.GetProperty("cancelledVouchers").GetInt32());
        Assert.Equal(1, audit.GetProperty("discardedProvisional").GetInt32());
        Assert.Equal(1, audit.GetProperty("reversedRedemptions").GetInt32());
    }

    // ---------- D-2 a D-4: origem, histórico e motivo ----------

    [Fact]
    public async Task Cancel_IsRecordedOnceInTheHistory_WithTheTrimmedReason_AndInTheAuditWithTheCounts()
    {
        var scenario = await ApprovedCampaignAsync(_factory);
        var satellite = Satellite(_factory);
        var first = await AvailableVoucherAsync(satellite, scenario, TestCpf.New());
        var second = await AvailableVoucherAsync(satellite, scenario, TestCpf.New());

        (await CancelAsync(User(_factory), scenario.CampaignId, $"  {Reason}  ")).EnsureSuccessStatusCode();

        var statusEvent = await QueryAsync(_factory, db => db.CampaignStatusEvents.AsNoTracking()
            .SingleAsync(item => item.CampaignId == scenario.CampaignId && item.ToStatus == CampaignStatus.Cancelled));
        Assert.Equal((CampaignStatus.Active, Reason), (statusEvent.FromStatus, statusEvent.Reason));

        var audit = await CancellationAuditAsync(scenario.CampaignId);
        Assert.Equal(Reason, audit.GetProperty("reason").GetString());
        Assert.Equal("Active", audit.GetProperty("fromStatus").GetString());
        Assert.Equal(2, audit.GetProperty("cancelledVouchers").GetInt32());
        Assert.Equal(0, audit.GetProperty("discardedProvisional").GetInt32());

        foreach (var voucherId in new[] { first.Id, second.Id })
        {
            var cancelledEvent = await QueryAsync(_factory, db => db.VoucherEvents.AsNoTracking()
                .SingleAsync(item => item.VoucherId == voucherId && item.EventType == VoucherEventTypes.Cancelled));
            Assert.Equal(AuditActorType.User, cancelledEvent.ActorType);
            Assert.Contains("CampaignCancelled", cancelledEvent.Payload);
        }

        // Sem auditoria por voucher (D-10).
        Assert.Equal(0, await QueryAsync(_factory, db => db.AuditEntries.CountAsync(entry =>
            entry.CampaignId == scenario.CampaignId && entry.EventType == AuditEventTypes.VoucherCancelled)));
    }

    [Fact]
    public async Task Cancel_FromScheduledPausedAndSoldOut_RecordsTheEffectiveStatus()
    {
        // Agendada: aprovada antes do início.
        _factory.Clock.UtcNow = Setup;
        var scheduled = await ReadyCampaign.CreateSubmittedAsync(User(_factory));
        (await User(_factory, Roles.Gestor).PostAsync($"{ReadyCampaign.Route}/{scheduled}/approve", null)).EnsureSuccessStatusCode();
        (await CancelAsync(User(_factory), scheduled)).EnsureSuccessStatusCode();

        // Pausada.
        var paused = await ApprovedCampaignAsync(_factory);
        (await User(_factory).PostAsync($"{ReadyCampaign.Route}/{paused.CampaignId}/pause", null)).EnsureSuccessStatusCode();
        (await CancelAsync(User(_factory), paused.CampaignId)).EnsureSuccessStatusCode();

        // Esgotada: total de 1, já emitido.
        var soldOut = await ApprovedCampaignAsync(_factory, (analista, id) =>
            analista.PutAsJsonAsync($"{ReadyCampaign.Route}/{id}/quantities", new { totalVoucherLimit = 1 }));
        var voucher = await AvailableVoucherAsync(Satellite(_factory), soldOut, TestCpf.New());
        Assert.Equal("SoldOut", await CampaignStatusAsync(_factory, soldOut.CampaignId));
        (await CancelAsync(User(_factory), soldOut.CampaignId)).EnsureSuccessStatusCode();

        Assert.Equal(CampaignStatus.Scheduled, await FromStatusAsync(scheduled));
        Assert.Equal(CampaignStatus.Paused, await FromStatusAsync(paused.CampaignId));
        Assert.Equal(CampaignStatus.SoldOut, await FromStatusAsync(soldOut.CampaignId));
        Assert.Equal(VoucherStatus.Cancelled, await StatusOfAsync(voucher.Id));
        Assert.Equal("Cancelled", await CampaignStatusAsync(_factory, soldOut.CampaignId));
    }

    [Fact]
    public async Task Cancel_FromDraftPendingRejectedEndedOrCancelled_Returns422()
    {
        _factory.Clock.UtcNow = Setup;
        var analista = User(_factory);
        var gestor = User(_factory, Roles.Gestor);
        var draft = await ReadyCampaign.CreateAsync(analista);
        var pending = await ReadyCampaign.CreateSubmittedAsync(analista);
        var rejected = await ReadyCampaign.CreateSubmittedAsync(analista);
        (await gestor.PostAsJsonAsync($"{ReadyCampaign.Route}/{rejected}/reject", new { reason = "Não" })).EnsureSuccessStatusCode();

        var ended = await ApprovedCampaignAsync(_factory);
        (await analista.PostAsync($"{ReadyCampaign.Route}/{ended.CampaignId}/end", null)).EnsureSuccessStatusCode();
        var cancelled = await ApprovedCampaignAsync(_factory);
        (await CancelAsync(analista, cancelled.CampaignId)).EnsureSuccessStatusCode();
        var pausedAfterTheEnd = await ApprovedCampaignAsync(_factory);
        (await analista.PostAsync($"{ReadyCampaign.Route}/{pausedAfterTheEnd.CampaignId}/pause", null)).EnsureSuccessStatusCode();

        foreach (var id in new[] { draft, pending, rejected, ended.CampaignId, cancelled.CampaignId })
        {
            await AssertNotAllowedAsync(await CancelAsync(analista, id));
        }

        _factory.Clock.UtcNow = new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.FromHours(-3));
        await AssertNotAllowedAsync(await CancelAsync(analista, pausedAfterTheEnd.CampaignId));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public async Task Cancel_WithoutReason_Returns400(string? reason)
    {
        var scenario = await ApprovedCampaignAsync(_factory);

        var response = await User(_factory).PostAsJsonAsync($"{ReadyCampaign.Route}/{scenario.CampaignId}/cancel", new { reason });

        await AssertValidationAsync(response, "Reason", CampaignContract.CancellationReasonRequired);
        Assert.Equal("Active", await CampaignStatusAsync(_factory, scenario.CampaignId));
    }

    [Fact]
    public async Task Cancel_WithAReasonLongerThan500_Returns400()
    {
        var scenario = await ApprovedCampaignAsync(_factory);

        var response = await CancelAsync(User(_factory), scenario.CampaignId, new string('x', 501));

        await AssertValidationAsync(response, "Reason", CampaignContract.CancellationReasonTooLong);
    }

    // ---------- D-13: estorno depois do cancelamento ----------

    [Fact]
    public async Task ReversingARedemptionAfterTheCancel_ReturnsTheUseButNotTheVoucher()
    {
        var scenario = await ApprovedCampaignAsync(_factory);
        var satellite = Satellite(_factory);
        var document = TestCpf.New();
        var voucher = await AvailableVoucherAsync(satellite, scenario, document);
        var redeemed = await RedeemedAsync(satellite, Redeem(voucher.Id, scenario.StoreId, document: document));
        (await CancelAsync(User(_factory), scenario.CampaignId)).EnsureSuccessStatusCode();
        Assert.Equal(VoucherStatus.Redeemed, await StatusOfAsync(voucher.Id));

        (await satellite.PostAsync($"{RedemptionsRoute}/{redeemed.RedemptionId}/reverse", null)).EnsureSuccessStatusCode();

        var stored = await QueryAsync(_factory, db => db.Vouchers.AsNoTracking().SingleAsync(item => item.Id == voucher.Id));
        Assert.Equal((VoucherStatus.Cancelled, 0), (stored.Status, stored.UsesCount));
    }

    // ---------- Concorrência (D-9, D-12) ----------

    [Fact]
    public async Task ConcurrentCancels_OnlyOneWins_AndEachVoucherHasASingleEvent()
    {
        var scenario = await ApprovedCampaignAsync(_factory);
        var voucher = await AvailableVoucherAsync(Satellite(_factory), scenario, TestCpf.New());

        var responses = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => CancelAsync(User(_factory), scenario.CampaignId)));

        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.OK);
        foreach (var refused in responses.Where(response => response.StatusCode != HttpStatusCode.OK))
        {
            Assert.Equal(HttpStatusCode.UnprocessableEntity, refused.StatusCode);
            Assert.Contains((await refused.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString(),
                new[] { CampaignErrors.ConcurrentUpdateCode, CampaignErrors.CancelNotAllowedCode });
        }

        Assert.Single(await EventsOfAsync(voucher.Id), type => type == VoucherEventTypes.Cancelled);
        Assert.Equal(1, await QueryAsync(_factory, db => db.AuditEntries.CountAsync(entry =>
            entry.CampaignId == scenario.CampaignId && entry.EventType == AuditEventTypes.CampaignCancelled)));
    }

    [Fact]
    public async Task ConcurrentGrantsAndCancel_LeaveNoValidVoucherInTheCancelledCampaign()
    {
        var scenario = await ApprovedCampaignAsync(_factory);
        var satellite = Satellite(_factory);

        var grants = Enumerable.Range(0, 10).Select(_ => GrantAsync(satellite, Purchase(scenario.StoreId, document: TestCpf.New()))).ToList();
        var cancel = CancelAsync(User(_factory), scenario.CampaignId);
        await Task.WhenAll(grants);
        (await cancel).EnsureSuccessStatusCode();

        Assert.All(grants, grant => Assert.True(grant.Result.Response.IsSuccessStatusCode, grant.Result.Response.StatusCode.ToString()));
        var alive = await QueryAsync(_factory, db => db.Vouchers.CountAsync(voucher => voucher.CampaignId == scenario.CampaignId
            && (voucher.Status == VoucherStatus.Provisional || voucher.Status == VoucherStatus.Available)));
        Assert.Equal(0, alive);
        var campaign = await StoredCampaignAsync(_factory, scenario.CampaignId);
        Assert.Equal(0, campaign.ReservedVouchers);
    }

    [Fact]
    public async Task ReservationOfAConcessionEvaluatedBeforeTheCancel_IsRefusedByTheStoredStatus()
    {
        var scenario = await ApprovedCampaignAsync(_factory);
        var itemId = (await StoredCampaignAsync(_factory, scenario.CampaignId)).Items.Single().Id;
        (await CancelAsync(User(_factory), scenario.CampaignId)).EnsureSuccessStatusCode();

        // A concessão leu a campanha ainda ativa e chega à reserva depois do commit do cancelamento (D-9).
        await using var scope = _factory.Services.CreateAsyncScope();
        var concessions = scope.ServiceProvider.GetRequiredService<IConcessionRepository>();
        await using var transaction = await concessions.BeginTransactionAsync(CancellationToken.None);
        var outcome = await concessions.TryReserveAsync(scenario.CampaignId, itemId, CancellationToken.None);

        Assert.Equal(ReserveOutcome.CampaignNotActive, outcome);
        Assert.Equal(0, (await StoredCampaignAsync(_factory, scenario.CampaignId)).ReservedVouchers);
    }

    [Fact]
    public async Task ConcurrentRedemptionsAndCancel_EachVoucherEndsRedeemedOrCancelled()
    {
        var scenario = await ApprovedCampaignAsync(_factory);
        var satellite = Satellite(_factory);
        var vouchers = new List<(Guid Id, string Document)>();

        for (var index = 0; index < 6; index++)
        {
            var document = TestCpf.New();
            vouchers.Add(((await AvailableVoucherAsync(satellite, scenario, document)).Id, document));
        }

        var redemptions = vouchers.Select(voucher => RedeemAsync(satellite, Redeem(voucher.Id, scenario.StoreId, document: voucher.Document))).ToList();
        var cancel = CancelAsync(User(_factory), scenario.CampaignId);
        await Task.WhenAll(redemptions);
        (await cancel).EnsureSuccessStatusCode();

        foreach (var (id, _) in vouchers)
        {
            var status = await StatusOfAsync(id);
            var effective = await QueryAsync(_factory, db => db.Redemptions.CountAsync(item => item.VoucherId == id));
            var cancelledEvents = (await EventsOfAsync(id)).Count(type => type == VoucherEventTypes.Cancelled);

            Assert.True(status is VoucherStatus.Redeemed or VoucherStatus.Cancelled, status.ToString());
            Assert.Equal(status == VoucherStatus.Redeemed ? (1, 0) : (0, 1), (effective, cancelledEvents));
        }
    }

    // ---------- Autorização e duplicação ----------

    [Fact]
    public async Task Cancel_RequiresAnAnalystOrManagerOfTheNetwork()
    {
        var scenario = await ApprovedCampaignAsync(_factory);
        var route = $"{ReadyCampaign.Route}/{scenario.CampaignId}/cancel";
        var body = new { reason = Reason };

        Assert.Equal(HttpStatusCode.Unauthorized, (await _factory.CreateClient().PostAsJsonAsync(route, body)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await User(_factory, Roles.Administrador).PostAsJsonAsync(route, body)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _factory.CreateClient()
            .WithBearer(TestAuth.CreateToken(_factory, Roles.Analista, TestAuth.OtherNetworkId)).PostAsJsonAsync(route, body)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await CancelAsync(User(_factory), Guid.NewGuid())).StatusCode);
        (await CancelAsync(User(_factory, Roles.Gestor), scenario.CampaignId)).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task CancelledCampaign_CanBeDuplicated()
    {
        var scenario = await ApprovedCampaignAsync(_factory);
        (await CancelAsync(User(_factory), scenario.CampaignId)).EnsureSuccessStatusCode();

        _factory.Clock.UtcNow = Setup;
        var copy = await User(_factory).PostAsJsonAsync($"{ReadyCampaign.Route}/{scenario.CampaignId}/duplicate", new { });

        Assert.Equal(HttpStatusCode.Created, copy.StatusCode);
        Assert.Equal("Draft", (await copy.Content.ReadFromJsonAsync<CampaignResponse>())!.Status);
    }

    // ---------- Apoio ----------

    private static Task<HttpResponseMessage> CancelAsync(HttpClient client, Guid campaignId, string reason = Reason) =>
        client.PostAsJsonAsync($"{ReadyCampaign.Route}/{campaignId}/cancel", new { reason });

    private Task<VoucherStatus> StatusOfAsync(Guid voucherId) =>
        QueryAsync(_factory, db => db.Vouchers.Where(voucher => voucher.Id == voucherId).Select(voucher => voucher.Status).SingleAsync());

    private Task<string> DocumentOfAsync(Guid voucherId) =>
        QueryAsync(_factory, db => db.Vouchers.Where(voucher => voucher.Id == voucherId).Select(voucher => voucher.CustomerDocument).SingleAsync());

    private Task<List<string>> EventsOfAsync(Guid voucherId) =>
        QueryAsync(_factory, db => db.VoucherEvents.Where(item => item.VoucherId == voucherId).Select(item => item.EventType).ToListAsync());

    private Task<CampaignStatus> FromStatusAsync(Guid campaignId) =>
        QueryAsync(_factory, db => db.CampaignStatusEvents
            .Where(item => item.CampaignId == campaignId && item.ToStatus == CampaignStatus.Cancelled)
            .Select(item => item.FromStatus)
            .SingleAsync());

    private async Task<JsonElement> CancellationAuditAsync(Guid campaignId)
    {
        var payload = await QueryAsync(_factory, db => db.AuditEntries.AsNoTracking()
            .Where(entry => entry.CampaignId == campaignId && entry.EventType == AuditEventTypes.CampaignCancelled)
            .Select(entry => entry.Payload)
            .SingleAsync());

        return JsonDocument.Parse(payload!).RootElement;
    }

    private static async Task AssertNotAllowedAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.UnprocessableEntity, body);
        Assert.Equal(CampaignErrors.CancelNotAllowedCode, JsonDocument.Parse(body).RootElement.GetProperty("code").GetString());
    }

    private static async Task AssertValidationAsync(HttpResponseMessage response, string field, string message)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.BadRequest, body);

        var errors = JsonDocument.Parse(body).RootElement.GetProperty("errors");
        var match = errors.EnumerateObject().SingleOrDefault(entry => string.Equals(entry.Name, field, StringComparison.OrdinalIgnoreCase));
        Assert.True(match.Value.ValueKind == JsonValueKind.Array, $"campo {field} ausente: {body}");
        Assert.Contains(message, match.Value.EnumerateArray().Select(item => item.GetString()));
    }
}
