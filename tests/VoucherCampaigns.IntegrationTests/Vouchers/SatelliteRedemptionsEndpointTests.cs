using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VoucherCampaigns.Application.Authentication;
using VoucherCampaigns.Application.Vouchers;
using VoucherCampaigns.Domain.Audit;
using VoucherCampaigns.Domain.Vouchers;
using VoucherCampaigns.Infrastructure.Persistence.Seed;
using VoucherCampaigns.IntegrationTests.Authentication;
using VoucherCampaigns.IntegrationTests.Campaigns;
using static VoucherCampaigns.IntegrationTests.Vouchers.MotorScenario;

namespace VoucherCampaigns.IntegrationTests.Vouchers;

/// <summary>Verificação de aplicáveis, resgate e estorno pela API (EP-05, US-07.3, spec GH-049), com o relógio fixo dentro da vigência.</summary>
public class SatelliteRedemptionsEndpointTests : IClassFixture<ApiFactory>
{
    private const string ShoesProductCode = "CAL-001";
    private readonly ApiFactory _factory;

    /// <summary>CPF próprio de cada teste: os vouchers dos outros testes da mesma fábrica não entram na verificação.</summary>
    private readonly string _document = Random.Shared.NextInt64(10_000_000_000, 99_999_999_999).ToString();

    public SatelliteRedemptionsEndpointTests(ApiFactory factory)
    {
        _factory = factory;
        _factory.Clock.UtcNow = Now;
    }

    [Fact]
    public async Task CheckThenRedeem_AnAvailableVoucher_ConsumesTheUseAndRecordsTheHistory()
    {
        var scenario = await ApprovedCampaignAsync(_factory);
        var satellite = Satellite(_factory);
        var voucher = await AvailableVoucherAsync(satellite, scenario);

        var check = await CheckAsync(satellite, Purchase(scenario.StoreId, "TRX-CHECK-1"));

        Assert.True(check.OnlyOnePerPurchase);
        Assert.Null(check.RedemptionId);
        var applicable = Assert.Single(check.Applicable);
        Assert.Equal(voucher.Id, applicable.Voucher.Id);
        Assert.Equal(20.00m, applicable.DiscountAmount);
        Assert.True(applicable.Recommended);
        Assert.Empty(check.NotApplicable);

        var redeemed = await RedeemedAsync(satellite, Redeem(voucher.Id, scenario.StoreId, "TRX-CHECK-1"));

        Assert.Equal("Effective", redeemed.Status);
        Assert.Equal("TRX-CHECK-1", redeemed.TransactionId);
        Assert.Equal(20.00m, redeemed.DiscountAmount);
        Assert.Equal("Redeemed", redeemed.Voucher.Status);
        Assert.Equal(0, redeemed.Voucher.RemainingUses);
        Assert.Equal(Now, redeemed.RedeemedAt);
        Assert.Null(redeemed.ReversedAt);

        var fetched = await satellite.GetFromJsonAsync<RedemptionDto>($"{RedemptionsRoute}/{redeemed.RedemptionId}");
        Assert.Equal(redeemed, fetched);

        var events = await QueryAsync(_factory, db => db.VoucherEvents.Where(e => e.VoucherId == voucher.Id).Select(e => e.EventType).ToListAsync());
        Assert.Contains(VoucherEventTypes.Redeemed, events);
        var audit = await QueryAsync(_factory, db => db.AuditEntries.SingleAsync(a => a.EventType == AuditEventTypes.VoucherRedeemed && a.EntityId == voucher.Id));
        Assert.Equal(AuditActorType.Application, audit.ActorType);
        Assert.Contains("TRX-CHECK-1", audit.Payload);

        var listed = await satellite.GetFromJsonAsync<List<VoucherResponseDto>>($"/api/v1/satellite/customers/{_document}/vouchers?status=Redeemed");
        Assert.Contains(listed!, item => item.Id == voucher.Id);
    }

    [Fact]
    public async Task Redeem_IsIdempotentPerTransaction_AndASecondVoucherInTheSamePurchaseIsRefused()
    {
        var scenario = await ApprovedCampaignAsync(_factory);
        var satellite = Satellite(_factory);
        var first = await AvailableVoucherAsync(satellite, scenario);
        var second = await AvailableVoucherAsync(satellite, scenario);

        var redeemed = await RedeemedAsync(satellite, Redeem(first.Id, scenario.StoreId, "TRX-IDEM"));
        var repeated = await RedeemedAsync(satellite, Redeem(first.Id, scenario.StoreId, "TRX-IDEM"));
        var (other, _) = await RedeemAsync(satellite, Redeem(second.Id, scenario.StoreId, "TRX-IDEM"));

        Assert.Equal(redeemed.RedemptionId, repeated.RedemptionId);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, other.StatusCode);
        Assert.Equal("REDEMPTION_TRANSACTION_ALREADY_REDEEMED", (await TestAuth.ReadProblemAsync(other)).GetProperty("code").GetString());

        var check = await CheckAsync(satellite, Purchase(scenario.StoreId, "TRX-IDEM"));
        Assert.Equal(redeemed.RedemptionId, check.RedemptionId);
        Assert.Empty(check.Applicable);
        Assert.Equal("AlreadyRedeemedInThisPurchase", check.NotApplicable.Single(entry => entry.Voucher.Id == first.Id).Reason);
        Assert.Equal("TransactionAlreadyRedeemed", check.NotApplicable.Single(entry => entry.Voucher.Id == second.Id).Reason);

        var uses = await QueryAsync(_factory, db => db.Vouchers.Where(v => v.Id == first.Id).Select(v => v.UsesCount).SingleAsync());
        Assert.Equal(1, uses);
    }

    [Fact]
    public async Task Redeem_AMultiUseVoucher_UntilTheLimit()
    {
        var scenario = await ApprovedCampaignAsync(_factory, (analista, id) =>
            analista.PutAsJsonAsync($"{ReadyCampaign.Route}/{id}/usage-restrictions", new { maxUsesPerVoucher = 3 }));
        var satellite = Satellite(_factory);
        var voucher = await AvailableVoucherAsync(satellite, scenario);

        var uses = new List<RedemptionDto>();
        for (var index = 0; index < 3; index++)
        {
            uses.Add(await RedeemedAsync(satellite, Redeem(voucher.Id, scenario.StoreId)));
        }

        Assert.Equal([2, 1, 0], uses.Select(use => use.Voucher.RemainingUses));
        Assert.Equal(["Available", "Available", "Redeemed"], uses.Select(use => use.Voucher.Status));

        var (fourth, _) = await RedeemAsync(satellite, Redeem(voucher.Id, scenario.StoreId));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, fourth.StatusCode);
        Assert.Equal("REDEMPTION_NO_USES_LEFT", (await TestAuth.ReadProblemAsync(fourth)).GetProperty("code").GetString());

        var redeemedEvents = await QueryAsync(_factory, db => db.VoucherEvents.CountAsync(e => e.VoucherId == voucher.Id && e.EventType == VoucherEventTypes.Redeemed));
        Assert.Equal(3, redeemedEvents);
    }

    [Fact]
    public async Task Check_ReportsTheRecommendedVoucher_AndTheReasonOfEachInapplicableOne()
    {
        // Três campanhas na mesma loja, criadas uma de cada vez para o motor (D1) escolher a nova: 10% sobre tudo (20,00),
        // R$ 27 fixos sobre tudo e 50% na categoria Calçados, cujo voucher nasce de uma compra só de calçados.
        var scenario = await ApprovedCampaignAsync(_factory);
        var storeId = scenario.StoreId;
        var satellite = Satellite(_factory);
        var percent = await AvailableVoucherAsync(satellite, scenario);

        var fixedCampaign = await ApprovedCampaignOnStoreAsync(storeId, (analista, id) =>
            analista.PutAsJsonAsync($"{ReadyCampaign.Route}/{id}/items", new { items = new[] { new { targetType = "All", benefitType = "FixedAmount", benefitValue = 27 } } }));
        var fixedGrant = await GrantedAsync(satellite, Purchase(storeId));
        Assert.Equal(fixedCampaign, fixedGrant.Voucher!.CampaignId);
        var fixedVoucher = (await ConfirmedAsync(satellite, fixedGrant.ConcessionId)).Voucher!;

        var shoesCampaign = await ApprovedCampaignOnStoreAsync(storeId, (analista, id) =>
            analista.PutAsJsonAsync($"{ReadyCampaign.Route}/{id}/items", new { items = new[] { new { targetType = "Category", categoryId = DemoDataSeeder.ShoesCategoryId, benefitType = "Percentage", benefitValue = 50 } } }));
        var shoesGrant = await GrantedAsync(satellite, Purchase(storeId, items: [new { productCode = ShoesProductCode, quantity = 1, unitPrice = 200m }]));
        Assert.Equal(shoesCampaign, shoesGrant.Voucher!.CampaignId);
        var shoes = (await ConfirmedAsync(satellite, shoesGrant.ConcessionId)).Voucher!;

        var check = await CheckAsync(satellite, Purchase(storeId, "TRX-RANK"));

        Assert.Equal([fixedVoucher.Id, percent.Id], check.Applicable.Select(entry => entry.Voucher.Id));
        Assert.Equal([27.00m, 20.00m], check.Applicable.Select(entry => entry.DiscountAmount));
        Assert.Equal([true, false], check.Applicable.Select(entry => entry.Recommended));
        var inapplicable = Assert.Single(check.NotApplicable);
        Assert.Equal(shoes.Id, inapplicable.Voucher.Id);
        Assert.Equal("TargetNotInPurchase", inapplicable.Reason);
        Assert.Equal("A compra não tem itens do alvo deste voucher.", inapplicable.Detail);
    }

    [Fact]
    public async Task Redeem_OutsideTheAmountRange_IsRefusedWithoutConsumingTheUse()
    {
        var scenario = await ApprovedCampaignAsync(_factory, (analista, id) =>
            analista.PutAsJsonAsync($"{ReadyCampaign.Route}/{id}/usage-restrictions", new { minPurchaseAmount = 150, maxPurchaseAmount = 300, maxUsesPerVoucher = 1 }));
        var satellite = Satellite(_factory);
        var voucher = await AvailableVoucherAsync(satellite, scenario);

        var (below, _) = await RedeemAsync(satellite, Redeem(voucher.Id, scenario.StoreId, totalAmount: 149.99m));
        var (above, _) = await RedeemAsync(satellite, Redeem(voucher.Id, scenario.StoreId, totalAmount: 300.01m));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, below.StatusCode);
        var problem = await TestAuth.ReadProblemAsync(below);
        Assert.Equal("REDEMPTION_AMOUNT_BELOW_MINIMUM", problem.GetProperty("code").GetString());
        Assert.Equal("A compra precisa ser de pelo menos R$ 150,00 para usar este voucher.", problem.GetProperty("detail").GetString());
        Assert.Equal("AmountBelowMinimum", problem.GetProperty("reason").GetString());
        Assert.Equal("REDEMPTION_AMOUNT_ABOVE_MAXIMUM", (await TestAuth.ReadProblemAsync(above)).GetProperty("code").GetString());

        var stored = await QueryAsync(_factory, db => db.Vouchers.AsNoTracking().SingleAsync(v => v.Id == voucher.Id));
        Assert.Equal(0, stored.UsesCount);
        Assert.Equal(VoucherStatus.Available, stored.Status);
        var refusals = await QueryAsync(_factory, db => db.AuditEntries.CountAsync(a => a.EventType == AuditEventTypes.VoucherRedemptionRefused && a.EntityId == voucher.Id));
        Assert.Equal(2, refusals);
    }

    [Fact]
    public async Task Redeem_InAStoreOrChannelOutsideTheScope_IsRefused()
    {
        var scenario = await ApprovedCampaignAsync(_factory);
        var satellite = Satellite(_factory);
        var voucher = await AvailableVoucherAsync(satellite, scenario);
        var otherStore = await NewStoreAsync(_factory, "Physical", "Virtual");

        var (outOfScope, _) = await RedeemAsync(satellite, Redeem(voucher.Id, otherStore));
        var (channel, _) = await RedeemAsync(satellite, Redeem(voucher.Id, scenario.StoreId, channel: "Virtual"));
        var (unknown, _) = await RedeemAsync(satellite, Redeem(voucher.Id, Guid.NewGuid()));

        Assert.Equal("REDEMPTION_OUT_OF_SCOPE", (await TestAuth.ReadProblemAsync(outOfScope)).GetProperty("code").GetString());
        Assert.Equal("REDEMPTION_CHANNEL_NOT_ENABLED", (await TestAuth.ReadProblemAsync(channel)).GetProperty("code").GetString());
        Assert.Equal("REDEMPTION_ESTABLISHMENT_NOT_FOUND", (await TestAuth.ReadProblemAsync(unknown)).GetProperty("code").GetString());

        var check = await CheckAsync(satellite, Purchase(otherStore));
        Assert.Equal("OutOfScope", Assert.Single(check.NotApplicable, entry => entry.Voucher.Id == voucher.Id).Reason);
    }

    [Fact]
    public async Task Redeem_AfterTheVoucherValidity_IsRefusedAndPersistsTheExpiration()
    {
        var scenario = await ApprovedCampaignAsync(_factory);
        var satellite = Satellite(_factory);
        var voucher = await AvailableVoucherAsync(satellite, scenario);

        // Validade calculada na emissão (RF18a), inclusiva até o último segundo (D9).
        _factory.Clock.UtcNow = voucher.ValidUntil;
        var stillValid = await CheckAsync(satellite, Purchase(scenario.StoreId));
        Assert.Contains(stillValid.Applicable, entry => entry.Voucher.Id == voucher.Id);

        _factory.Clock.UtcNow = voucher.ValidUntil.AddSeconds(1);
        var check = await CheckAsync(satellite, Purchase(scenario.StoreId));
        Assert.Equal("Expired", Assert.Single(check.NotApplicable, entry => entry.Voucher.Id == voucher.Id).Reason);

        var (response, _) = await RedeemAsync(satellite, Redeem(voucher.Id, scenario.StoreId));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var problem = await TestAuth.ReadProblemAsync(response);
        Assert.Equal("REDEMPTION_EXPIRED", problem.GetProperty("code").GetString());
        Assert.Equal($"O voucher venceu em {voucher.ValidUntil:dd/MM/yyyy HH:mm:ss}.", problem.GetProperty("detail").GetString());

        var stored = await QueryAsync(_factory, db => db.Vouchers.AsNoTracking().SingleAsync(v => v.Id == voucher.Id));
        Assert.Equal(VoucherStatus.Expired, stored.Status);
        var events = await QueryAsync(_factory, db => db.VoucherEvents.Where(e => e.VoucherId == voucher.Id).Select(e => e.EventType).ToListAsync());
        Assert.Contains(VoucherEventTypes.Expired, events);
    }

    [Fact]
    public async Task Redeem_AfterTheCampaignWasPausedOrEnded_StillWorks()
    {
        var scenario = await ApprovedCampaignAsync(_factory);
        var satellite = Satellite(_factory);
        var voucher = await AvailableVoucherAsync(satellite, scenario);
        (await User(_factory, Roles.Gestor).PostAsync($"{ReadyCampaign.Route}/{scenario.CampaignId}/pause", null)).EnsureSuccessStatusCode();
        Assert.Equal("Paused", await CampaignStatusAsync(_factory, scenario.CampaignId));

        var redeemed = await RedeemedAsync(satellite, Redeem(voucher.Id, scenario.StoreId));

        Assert.Equal("Redeemed", redeemed.Voucher.Status);
    }

    [Fact]
    public async Task SamePurchase_WhenAllowed_RedeemsTheProvisionalVoucher_AndTheConfirmationSettlesIt()
    {
        var scenario = await ApprovedCampaignAsync(_factory, (analista, id) =>
            analista.PutAsJsonAsync($"{ReadyCampaign.Route}/{id}/conditions", new { conditions = Array.Empty<object>(), allowUseInSamePurchase = true }));
        var satellite = Satellite(_factory);

        var granted = await GrantedAsync(satellite, Purchase(scenario.StoreId, "TRX-MESMA"));
        var check = await CheckAsync(satellite, Purchase(scenario.StoreId, "TRX-MESMA"));
        var applicable = Assert.Single(check.Applicable);
        Assert.Equal(granted.Voucher!.Id, applicable.Voucher.Id);
        Assert.Equal("Provisional", applicable.Voucher.Status);

        var redeemed = await RedeemedAsync(satellite, Redeem(granted.Voucher.Id, scenario.StoreId, "TRX-MESMA"));
        Assert.Equal("Provisional", redeemed.Voucher.Status);
        Assert.Equal(0, redeemed.Voucher.RemainingUses);

        var confirmed = await ConfirmedAsync(satellite, granted.ConcessionId);
        Assert.Equal("Confirmed", confirmed.Status);
        Assert.Equal("Redeemed", confirmed.Voucher!.Status);
        var stored = await StoredCampaignAsync(_factory, scenario.CampaignId);
        Assert.Equal(1, stored.IssuedVouchers);
        Assert.Equal(0, stored.ReservedVouchers);

        // Outra compra do mesmo cliente não pode usar um provisório que não é dela.
        var secondGrant = await GrantedAsync(satellite, Purchase(scenario.StoreId, "TRX-MESMA-2"));
        var otherCheck = await CheckAsync(satellite, Purchase(scenario.StoreId, "TRX-ALHEIA"));
        Assert.Equal("NotConfirmed", Assert.Single(otherCheck.NotApplicable, entry => entry.Voucher.Id == secondGrant.Voucher!.Id).Reason);
    }

    [Fact]
    public async Task SamePurchase_WhenNotAllowed_TheProvisionalVoucherIsNotApplicable()
    {
        var scenario = await ApprovedCampaignAsync(_factory);
        var satellite = Satellite(_factory);
        var granted = await GrantedAsync(satellite, Purchase(scenario.StoreId, "TRX-PROIBIDA"));

        var check = await CheckAsync(satellite, Purchase(scenario.StoreId, "TRX-PROIBIDA"));
        var (response, _) = await RedeemAsync(satellite, Redeem(granted.Voucher!.Id, scenario.StoreId, "TRX-PROIBIDA"));

        Assert.Empty(check.Applicable);
        Assert.Equal("SamePurchaseNotAllowed", Assert.Single(check.NotApplicable).Reason);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("REDEMPTION_SAME_PURCHASE_NOT_ALLOWED", (await TestAuth.ReadProblemAsync(response)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task SamePurchase_CancellingTheConcession_ReversesTheRedemption()
    {
        var scenario = await ApprovedCampaignAsync(_factory, (analista, id) =>
            analista.PutAsJsonAsync($"{ReadyCampaign.Route}/{id}/conditions", new { conditions = Array.Empty<object>(), allowUseInSamePurchase = true }));
        var satellite = Satellite(_factory);
        var granted = await GrantedAsync(satellite, Purchase(scenario.StoreId, "TRX-DESFEITA"));
        var redeemed = await RedeemedAsync(satellite, Redeem(granted.Voucher!.Id, scenario.StoreId, "TRX-DESFEITA"));

        var cancel = await satellite.PostAsync($"{ConcessionsRoute}/{granted.ConcessionId}/cancel", null);
        cancel.EnsureSuccessStatusCode();

        var redemption = await satellite.GetFromJsonAsync<RedemptionDto>($"{RedemptionsRoute}/{redeemed.RedemptionId}");
        Assert.Equal("Reversed", redemption!.Status);
        Assert.Equal(Now, redemption.ReversedAt);
        Assert.Equal("Discarded", redemption.Voucher.Status);
        var stored = await StoredCampaignAsync(_factory, scenario.CampaignId);
        Assert.Equal(0, stored.IssuedVouchers + stored.ReservedVouchers);
        var events = await QueryAsync(_factory, db => db.VoucherEvents.Where(e => e.VoucherId == granted.Voucher.Id).Select(e => e.EventType).ToListAsync());
        Assert.Contains(VoucherEventTypes.RedemptionReversed, events);
        Assert.Contains(VoucherEventTypes.Cancelled, events);
    }

    [Fact]
    public async Task SamePurchase_WhenTheReservationExpires_TheSweepReversesTheRedemption()
    {
        var scenario = await ApprovedCampaignAsync(_factory, (analista, id) =>
            analista.PutAsJsonAsync($"{ReadyCampaign.Route}/{id}/conditions", new { conditions = Array.Empty<object>(), allowUseInSamePurchase = true }));
        var satellite = Satellite(_factory);
        var granted = await GrantedAsync(satellite, Purchase(scenario.StoreId, "TRX-ESQUECIDA"));
        var redeemed = await RedeemedAsync(satellite, Redeem(granted.Voucher!.Id, scenario.StoreId, "TRX-ESQUECIDA"));

        var later = Now.AddMinutes(16).UtcDateTime;
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            // Outros testes da mesma fábrica também podem ter reservas vencidas; basta a desta ter sido liberada.
            Assert.True(await scope.ServiceProvider.GetRequiredService<IReservationReleaser>().ReleaseExpiredAsync(DemoDataSeeder.NetworkId, later, CancellationToken.None) >= 1);
        }

        _factory.Clock.UtcNow = Now.AddMinutes(16);
        var redemption = await satellite.GetFromJsonAsync<RedemptionDto>($"{RedemptionsRoute}/{redeemed.RedemptionId}");
        Assert.Equal("Reversed", redemption!.Status);
        Assert.Equal("Discarded", redemption.Voucher.Status);
        var stored = await StoredCampaignAsync(_factory, scenario.CampaignId);
        Assert.Equal(0, stored.IssuedVouchers + stored.ReservedVouchers);
    }

    [Fact]
    public async Task Reverse_ReturnsTheUse_AndIsIdempotent()
    {
        var scenario = await ApprovedCampaignAsync(_factory);
        var satellite = Satellite(_factory);
        var voucher = await AvailableVoucherAsync(satellite, scenario);
        var other = await AvailableVoucherAsync(satellite, scenario);
        var redeemed = await RedeemedAsync(satellite, Redeem(voucher.Id, scenario.StoreId, "TRX-ESTORNO"));
        Assert.Equal("Redeemed", redeemed.Voucher.Status);

        var reverse = await satellite.PostAsync($"{RedemptionsRoute}/{redeemed.RedemptionId}/reverse", null);
        reverse.EnsureSuccessStatusCode();
        var reversed = (await reverse.Content.ReadFromJsonAsync<RedemptionDto>())!;
        var again = await (await satellite.PostAsync($"{RedemptionsRoute}/{redeemed.RedemptionId}/reverse", null)).Content.ReadFromJsonAsync<RedemptionDto>();

        Assert.Equal("Reversed", reversed.Status);
        Assert.Equal(Now, reversed.ReversedAt);
        Assert.Equal("Available", reversed.Voucher.Status);
        Assert.Equal(1, reversed.Voucher.RemainingUses);
        Assert.Equal(reversed, again);

        // A transação estornada continua fechada: outro voucher não entra nela, mas o voucher volta a servir em outra compra.
        var (sameTransaction, _) = await RedeemAsync(satellite, Redeem(other.Id, scenario.StoreId, "TRX-ESTORNO"));
        Assert.Equal("REDEMPTION_TRANSACTION_ALREADY_REDEEMED", (await TestAuth.ReadProblemAsync(sameTransaction)).GetProperty("code").GetString());
        var retry = await RedeemedAsync(satellite, Redeem(voucher.Id, scenario.StoreId, "TRX-NOVA"));
        Assert.Equal("Redeemed", retry.Voucher.Status);

        var audit = await QueryAsync(_factory, db => db.AuditEntries.CountAsync(a => a.EventType == AuditEventTypes.VoucherRedemptionReversed && a.EntityId == voucher.Id));
        Assert.Equal(1, audit);
    }

    [Fact]
    public async Task Reverse_OnAMultiUseVoucher_ReversesOnlyTheRequestedRedemption()
    {
        var scenario = await ApprovedCampaignAsync(_factory, (analista, id) =>
            analista.PutAsJsonAsync($"{ReadyCampaign.Route}/{id}/usage-restrictions", new { maxUsesPerVoucher = 2 }));
        var satellite = Satellite(_factory);
        var voucher = await AvailableVoucherAsync(satellite, scenario);
        var first = await RedeemedAsync(satellite, Redeem(voucher.Id, scenario.StoreId, "TRX-MULTI-1"));
        var second = await RedeemedAsync(satellite, Redeem(voucher.Id, scenario.StoreId, "TRX-MULTI-2"));
        Assert.Equal("Redeemed", second.Voucher.Status);

        var reversed = await (await satellite.PostAsync($"{RedemptionsRoute}/{second.RedemptionId}/reverse", null)).Content.ReadFromJsonAsync<RedemptionDto>();
        var untouched = await satellite.GetFromJsonAsync<RedemptionDto>($"{RedemptionsRoute}/{first.RedemptionId}");

        Assert.Equal("Reversed", reversed!.Status);
        Assert.Equal("Effective", untouched!.Status);
        Assert.Equal("Available", reversed.Voucher.Status);
        Assert.Equal(1, reversed.Voucher.RemainingUses);
        var stored = await QueryAsync(_factory, db => db.Vouchers.AsNoTracking().SingleAsync(v => v.Id == voucher.Id));
        Assert.Equal(1, stored.UsesCount);
        Assert.Equal(1, await QueryAsync(_factory, db => db.VoucherEvents.CountAsync(e => e.VoucherId == voucher.Id && e.EventType == VoucherEventTypes.RedemptionReversed)));

        // O estorno do primeiro devolve a outra utilização: nenhum resgate ativo, voucher inteiro de novo.
        var reversedFirst = await (await satellite.PostAsync($"{RedemptionsRoute}/{first.RedemptionId}/reverse", null)).Content.ReadFromJsonAsync<RedemptionDto>();
        Assert.Equal(2, reversedFirst!.Voucher.RemainingUses);
    }

    [Fact]
    public async Task Redeem_AnUnknownVoucher_OrOneOfAnotherCustomerOrNetwork_IsNotFound()
    {
        var scenario = await ApprovedCampaignAsync(_factory);
        var satellite = Satellite(_factory);
        var voucher = await AvailableVoucherAsync(satellite, scenario);

        var (unknown, _) = await RedeemAsync(satellite, Redeem(Guid.NewGuid(), scenario.StoreId));
        var (otherCustomer, _) = await RedeemAsync(satellite, Redeem(voucher.Id, scenario.StoreId, document: "98765432100"));
        var otherNetwork = await Satellite(_factory, networkId: Guid.NewGuid()).GetAsync($"{RedemptionsRoute}/{Guid.NewGuid()}");

        foreach (var response in new[] { unknown, otherCustomer, otherNetwork })
        {
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Equal("NOT_FOUND", (await TestAuth.ReadProblemAsync(response)).GetProperty("code").GetString());
        }
    }

    [Fact]
    public async Task Routes_RequireTheRedeemScope_AndRejectUserTokens()
    {
        var scenario = await ApprovedCampaignAsync(_factory);
        var issueOnly = Satellite(_factory, scopes: [SatelliteScopes.IssueVouchers]);
        var user = User(_factory);

        var forbidden = await issueOnly.PostAsJsonAsync(CheckRoute, Purchase(scenario.StoreId));
        var unauthorized = await user.PostAsJsonAsync(RedemptionsRoute, Redeem(Guid.NewGuid(), scenario.StoreId));
        var invalid = await Satellite(_factory).PostAsJsonAsync(RedemptionsRoute, new { transactionId = "x" });

        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Equal("VALIDATION_ERROR", (await TestAuth.ReadProblemAsync(invalid)).GetProperty("code").GetString());
    }

    private object Purchase(Guid establishmentId, string? transactionId = null, string channel = "Physical", decimal totalAmount = 200m, object[]? items = null) =>
        MotorScenario.Purchase(establishmentId, transactionId, _document, channel, totalAmount, items);

    private object Redeem(Guid voucherId, Guid establishmentId, string? transactionId = null, string? document = null, string channel = "Physical", decimal totalAmount = 200m, object[]? items = null) =>
        MotorScenario.Redeem(voucherId, establishmentId, transactionId, document ?? _document, channel, totalAmount, items);

    private Task<VoucherResponseDto> AvailableVoucherAsync(HttpClient satellite, Scenario scenario) =>
        MotorScenario.AvailableVoucherAsync(satellite, scenario, _document);

    /// <summary>Outra campanha aprovada na mesma loja do cenário, com a configuração extra.</summary>
    private async Task<Guid> ApprovedCampaignOnStoreAsync(Guid storeId, Func<HttpClient, Guid, Task> configure)
    {
        _factory.Clock.UtcNow = Setup;
        var analista = User(_factory);
        var id = await ReadyCampaign.CreateAsync(analista);
        (await analista.PutAsJsonAsync($"{ReadyCampaign.Route}/{id}/scopes", new { scopes = new[] { new { establishmentId = storeId, channels = new[] { "Physical" } } } }))
            .EnsureSuccessStatusCode();
        await configure(analista, id);
        (await analista.PostAsync($"{ReadyCampaign.Route}/{id}/submit", null)).EnsureSuccessStatusCode();
        (await User(_factory, Roles.Gestor).PostAsync($"{ReadyCampaign.Route}/{id}/approve", null)).EnsureSuccessStatusCode();
        _factory.Clock.UtcNow = Now;

        return id;
    }
}
