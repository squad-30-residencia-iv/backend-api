using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VoucherCampaigns.Application.Authentication;
using VoucherCampaigns.Domain.Audit;
using VoucherCampaigns.Domain.Campaigns;
using VoucherCampaigns.Domain.Vouchers;
using VoucherCampaigns.Infrastructure.Persistence;
using VoucherCampaigns.Infrastructure.Persistence.Seed;
using VoucherCampaigns.IntegrationTests.Authentication;
using VoucherCampaigns.IntegrationTests.Campaigns;
using static VoucherCampaigns.IntegrationTests.Vouchers.MotorScenario;

namespace VoucherCampaigns.IntegrationTests.Vouchers;

/// <summary>Concessão em duas fases pela API (US-04.x, US-07.2), com o relógio fixo dentro da vigência.</summary>
public class SatelliteConcessionsEndpointTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public SatelliteConcessionsEndpointTests(ApiFactory factory)
    {
        _factory = factory;
        _factory.Clock.UtcNow = Now;
    }

    [Fact]
    public async Task Grant_WhenACampaignIsSatisfied_IssuesAProvisionalVoucherAndReservesTheBalance()
    {
        var scenario = await ApprovedCampaignAsync(_factory);
        var satellite = Satellite(_factory);

        var body = await GrantedAsync(satellite, Purchase(scenario.StoreId, "TRX-98765"));

        Assert.Equal("TRX-98765", body.TransactionId);
        Assert.Null(body.Reason);
        Assert.Equal(Now.AddMinutes(15), body.ReservedUntil);
        Assert.Equal(TimeSpan.FromHours(-3), body.ReservedUntil!.Value.Offset);
        var voucher = body.Voucher!;
        Assert.Matches("^[0-9ABCDEFGHJKMNPQRSTVWXYZ]{12}$", voucher.Code);
        Assert.Equal(scenario.CampaignId, voucher.CampaignId);
        Assert.Equal("Provisional", voucher.Status);
        Assert.Equal(Now, voucher.IssuedAt);
        Assert.Equal(Now.AddDays(30), voucher.ValidUntil);
        Assert.Equal(1, voucher.MaxUses);
        Assert.Equal(1, voucher.RemainingUses);
        Assert.False(voucher.AllowUseInSamePurchase);
        Assert.Equal("All", voucher.Target.TargetType);
        Assert.Equal("Percentage", voucher.Target.BenefitType);
        Assert.Equal(10m, voucher.Target.BenefitValue);
        Assert.Equal(body.ConcessionId, voucher.ConcessionId);

        var stored = await StoredCampaignAsync(_factory, scenario.CampaignId);
        Assert.Equal(0, stored.IssuedVouchers);
        Assert.Equal(1, stored.ReservedVouchers);
        Assert.Equal(1, Assert.Single(stored.Items).ReservedVouchers);

        var storedVoucher = await QueryAsync(_factory, context => context.Vouchers.Include(item => item.Events).SingleAsync(item => item.Id == voucher.Id));
        Assert.Equal(DocumentDigits, storedVoucher.CustomerDocument);
        Assert.Equal(VoucherStatus.Provisional, storedVoucher.Status);
        Assert.Equal(scenario.StoreId, storedVoucher.EstablishmentId);
        var issued = Assert.Single(storedVoucher.Events);
        Assert.Equal(VoucherEventTypes.Issued, issued.EventType);
        Assert.Equal(AuditActorType.Application, issued.ActorType);
        Assert.Contains("\"audienceType\":\"AllCustomers\"", issued.Payload);

        var audit = await QueryAsync(_factory, context => context.AuditEntries.SingleAsync(entry => entry.EntityId == voucher.Id));
        Assert.Equal(AuditEventTypes.VoucherIssued, audit.EventType);
        Assert.Equal(AuditActorType.Application, audit.ActorType);
        Assert.Equal(DemoDataSeeder.NetworkId, audit.NetworkId);
        Assert.Equal(scenario.CampaignId, audit.CampaignId);
        Assert.Equal(Now.UtcDateTime, audit.OccurredAt);
        Assert.Contains(voucher.Code, audit.Payload);
        var app = await satellite.GetFromJsonAsync<SatelliteAppResponse>("/api/v1/satellite/me");
        Assert.Equal(app!.ApplicationId, audit.ActorId);
    }

    [Fact]
    public async Task Confirm_MakesTheVoucherAvailableAndConsumesTheBalance_Idempotently()
    {
        var scenario = await ApprovedCampaignAsync(_factory);
        var satellite = Satellite(_factory);
        var granted = await GrantedAsync(satellite, Purchase(scenario.StoreId));

        var confirmed = await satellite.PostAsync($"{ConcessionsRoute}/{granted.ConcessionId}/confirm", null);
        var again = await satellite.PostAsync($"{ConcessionsRoute}/{granted.ConcessionId}/confirm", null);

        Assert.Equal(HttpStatusCode.OK, confirmed.StatusCode);
        var body = await confirmed.Content.ReadFromJsonAsync<ConcessionResponseDto>();
        Assert.Equal("Confirmed", body!.Status);
        Assert.Null(body.ReservedUntil);
        Assert.Equal("Available", body.Voucher!.Status);
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.Equal("Confirmed", (await again.Content.ReadFromJsonAsync<ConcessionResponseDto>())!.Status);

        var stored = await StoredCampaignAsync(_factory, scenario.CampaignId);
        Assert.Equal(1, stored.IssuedVouchers);
        Assert.Equal(0, stored.ReservedVouchers);
        Assert.Equal(1, Assert.Single(stored.Items).IssuedVouchers);
        // Os dois eventos têm o mesmo instante (relógio fixo), então a ordem pelo id v7 não é determinística.
        var events = await QueryAsync(_factory, context => context.VoucherEvents.Where(item => item.VoucherId == granted.Voucher!.Id).ToListAsync());
        Assert.Equal([VoucherEventTypes.Confirmed, VoucherEventTypes.Issued], events.Select(item => item.EventType).Order());
        Assert.Equal(1, await QueryAsync(_factory, context => context.AuditEntries.CountAsync(entry => entry.EventType == AuditEventTypes.VoucherConfirmed && entry.EntityId == granted.Voucher!.Id)));
        Assert.Equal("Active", await CampaignStatusAsync(_factory, scenario.CampaignId));
    }

    [Fact]
    public async Task Cancel_DiscardsTheVoucherAndReleasesTheBalance()
    {
        var scenario = await ApprovedCampaignAsync(_factory);
        var satellite = Satellite(_factory);
        var granted = await GrantedAsync(satellite, Purchase(scenario.StoreId));

        var cancelled = await satellite.PostAsync($"{ConcessionsRoute}/{granted.ConcessionId}/cancel", null);
        var cancelAgain = await satellite.PostAsync($"{ConcessionsRoute}/{granted.ConcessionId}/cancel", null);
        var confirmAfter = await satellite.PostAsync($"{ConcessionsRoute}/{granted.ConcessionId}/confirm", null);

        Assert.Equal(HttpStatusCode.OK, cancelled.StatusCode);
        var body = await cancelled.Content.ReadFromJsonAsync<ConcessionResponseDto>();
        Assert.Equal("Cancelled", body!.Status);
        Assert.Equal("Discarded", body.Voucher!.Status);
        Assert.Equal(HttpStatusCode.OK, cancelAgain.StatusCode);
        await AssertProblemAsync(confirmAfter, HttpStatusCode.UnprocessableEntity, ConcessionErrors.AlreadyCancelledCode);

        var stored = await StoredCampaignAsync(_factory, scenario.CampaignId);
        Assert.Equal(0, stored.IssuedVouchers);
        Assert.Equal(0, stored.ReservedVouchers);
        Assert.Equal(0, Assert.Single(stored.Items).ReservedVouchers);
        Assert.Equal(1, await QueryAsync(_factory, context => context.AuditEntries.CountAsync(entry => entry.EventType == AuditEventTypes.VoucherCancelled && entry.EntityId == granted.Voucher!.Id)));
    }

    [Fact]
    public async Task Cancel_AfterConfirm_IsRefused()
    {
        var scenario = await ApprovedCampaignAsync(_factory);
        var satellite = Satellite(_factory);
        var granted = await GrantedAsync(satellite, Purchase(scenario.StoreId));
        (await satellite.PostAsync($"{ConcessionsRoute}/{granted.ConcessionId}/confirm", null)).EnsureSuccessStatusCode();

        var cancel = await satellite.PostAsync($"{ConcessionsRoute}/{granted.ConcessionId}/cancel", null);

        await AssertProblemAsync(cancel, HttpStatusCode.UnprocessableEntity, ConcessionErrors.AlreadyConfirmedCode);
        Assert.Equal(1, (await StoredCampaignAsync(_factory, scenario.CampaignId)).IssuedVouchers);
    }

    [Fact]
    public async Task Confirm_AfterTheReservationExpired_IsRefusedAndTheBalanceIsReleased()
    {
        var scenario = await ApprovedCampaignAsync(_factory);
        var satellite = Satellite(_factory);
        var granted = await GrantedAsync(satellite, Purchase(scenario.StoreId));

        _factory.Clock.UtcNow = Now.AddMinutes(16);
        var confirm = await satellite.PostAsync($"{ConcessionsRoute}/{granted.ConcessionId}/confirm", null);
        var state = await satellite.GetFromJsonAsync<ConcessionResponseDto>($"{ConcessionsRoute}/{granted.ConcessionId}");
        var cancel = await satellite.PostAsync($"{ConcessionsRoute}/{granted.ConcessionId}/cancel", null);
        _factory.Clock.UtcNow = Now;

        await AssertProblemAsync(confirm, HttpStatusCode.UnprocessableEntity, ConcessionErrors.ReservationExpiredCode);
        Assert.Equal("Expired", state!.Status);
        Assert.Equal("Discarded", state.Voucher!.Status);
        Assert.Equal(HttpStatusCode.OK, cancel.StatusCode);
        var stored = await StoredCampaignAsync(_factory, scenario.CampaignId);
        Assert.Equal(0, stored.ReservedVouchers);
        var events = await QueryAsync(_factory, context => context.VoucherEvents.Where(item => item.VoucherId == granted.Voucher!.Id).OrderBy(item => item.Id).ToListAsync());
        Assert.Equal(VoucherEventTypes.ReservationExpired, events.Last().EventType);
        Assert.Null(events.Last().ActorType);
    }

    [Fact]
    public async Task Grant_RepeatedTransaction_ReturnsTheOriginalConcessionWithoutReservingAgain()
    {
        var scenario = await ApprovedCampaignAsync(_factory);
        var satellite = Satellite(_factory);
        var purchase = Purchase(scenario.StoreId, "TRX-REPETIDA");

        var first = await GrantedAsync(satellite, purchase);
        var second = await GrantedAsync(satellite, purchase);
        var third = await GrantedAsync(satellite, Purchase(scenario.StoreId, "TRX-OUTRA"));

        Assert.Equal(first.ConcessionId, second.ConcessionId);
        Assert.Equal(first.Voucher!.Code, second.Voucher!.Code);
        Assert.NotEqual(first.ConcessionId, third.ConcessionId);
        Assert.NotEqual(first.Voucher.Code, third.Voucher!.Code);
        Assert.Equal(2, (await StoredCampaignAsync(_factory, scenario.CampaignId)).ReservedVouchers);
    }

    [Fact]
    public async Task Grant_WithoutASatisfiedCampaign_IsNotGrantedAndIsStillIdempotent()
    {
        var scenario = await ApprovedCampaignAsync(_factory, (analista, id) =>
            analista.PutAsJsonAsync($"{ReadyCampaign.Route}/{id}/conditions", new { conditions = new[] { new { type = "PurchaseTotal", minAmount = 150 } }, allowUseInSamePurchase = false }));
        var satellite = Satellite(_factory);
        var purchase = Purchase(scenario.StoreId, "TRX-BAIXA", totalAmount: 149.99m, items: [new { productCode = "MOD-001", quantity = 1, unitPrice = 149.99m }]);

        var body = await NotGrantedAsync(satellite, purchase, "NoCampaignMatched");
        var (_, replay) = await GrantAsync(satellite, purchase);
        var granted = await GrantedAsync(satellite, Purchase(scenario.StoreId, totalAmount: 150m, items: [new { productCode = "MOD-001", quantity = 1, unitPrice = 150m }]));

        Assert.Null(body.ReservedUntil);
        Assert.Equal(body.ConcessionId, replay!.ConcessionId);
        Assert.Equal(0, await QueryAsync(_factory, context => context.AuditEntries.CountAsync(entry => entry.EntityId == body.ConcessionId)));
        Assert.NotNull(granted.Voucher);
    }

    // Esquema da US-04.4: só a efetivamente ativa concede.
    [Theory]
    [InlineData("Draft", "NoCampaignMatched")]
    [InlineData("PendingApproval", "NoCampaignMatched")]
    [InlineData("Rejected", "NoCampaignMatched")]
    [InlineData("Scheduled", "CampaignNotActive")]
    [InlineData("Paused", "CampaignNotActive")]
    [InlineData("Ended", "CampaignNotActive")]
    public async Task Grant_WhenTheCampaignIsNotEffectivelyActive_IsNotGranted(string status, string reason)
    {
        _factory.Clock.UtcNow = Setup;
        var analista = User(_factory);
        var gestor = User(_factory, Roles.Gestor);
        var storeId = await NewStoreAsync(_factory);
        var id = await ReadyCampaign.CreateAsync(analista);
        (await analista.PutAsJsonAsync($"{ReadyCampaign.Route}/{id}/scopes", new { scopes = new[] { new { establishmentId = storeId, channels = new[] { "Physical" } } } })).EnsureSuccessStatusCode();

        if (status != "Draft")
        {
            (await analista.PostAsync($"{ReadyCampaign.Route}/{id}/submit", null)).EnsureSuccessStatusCode();
        }

        switch (status)
        {
            case "Rejected":
                (await gestor.PostAsync($"{ReadyCampaign.Route}/{id}/reject", null)).EnsureSuccessStatusCode();
                break;
            case "Scheduled":
                // Aprovada antes do início; a concessão também é tentada antes do início.
                (await gestor.PostAsync($"{ReadyCampaign.Route}/{id}/approve", null)).EnsureSuccessStatusCode();
                break;
            case "Paused":
            case "Ended":
                (await gestor.PostAsync($"{ReadyCampaign.Route}/{id}/approve", null)).EnsureSuccessStatusCode();
                _factory.Clock.UtcNow = Now;
                (await analista.PostAsync($"{ReadyCampaign.Route}/{id}/{(status == "Paused" ? "pause" : "end")}", null)).EnsureSuccessStatusCode();
                break;
        }

        if (status != "Scheduled")
        {
            _factory.Clock.UtcNow = Now;
        }

        var (_, body) = await GrantAsync(Satellite(_factory), Purchase(storeId));
        _factory.Clock.UtcNow = Now;

        Assert.Equal("NotGranted", body!.Status);
        Assert.Equal(reason, body.Reason);
    }

    [Fact]
    public async Task Grant_ScheduledCampaignWhoseStartArrived_Grants()
    {
        _factory.Clock.UtcNow = Setup;
        var analista = User(_factory);
        var storeId = await NewStoreAsync(_factory);
        var id = await ReadyCampaign.CreateAsync(analista);
        (await analista.PutAsJsonAsync($"{ReadyCampaign.Route}/{id}/scopes", new { scopes = new[] { new { establishmentId = storeId, channels = new[] { "Physical" } } } })).EnsureSuccessStatusCode();
        (await analista.PostAsync($"{ReadyCampaign.Route}/{id}/submit", null)).EnsureSuccessStatusCode();
        (await User(_factory, Roles.Gestor).PostAsync($"{ReadyCampaign.Route}/{id}/approve", null)).EnsureSuccessStatusCode();
        _factory.Clock.UtcNow = Now;

        var granted = await GrantedAsync(Satellite(_factory), Purchase(storeId));

        Assert.Equal(id, granted.Voucher!.CampaignId);
        Assert.Equal(CampaignStatus.Scheduled, (await StoredCampaignAsync(_factory, id)).Status);
    }

    [Fact]
    public async Task Grant_WhenTheTotalIsReached_IsRefusedAndTheCampaignShowsSoldOut()
    {
        var scenario = await ApprovedCampaignAsync(_factory, (analista, id) =>
            analista.PutAsJsonAsync($"{ReadyCampaign.Route}/{id}/quantities", new { totalVoucherLimit = 1 }));
        var satellite = Satellite(_factory);

        var first = await GrantedAsync(satellite, Purchase(scenario.StoreId));
        var second = await NotGrantedAsync(satellite, Purchase(scenario.StoreId), "CampaignSoldOut");
        var soldOut = await CampaignStatusAsync(_factory, scenario.CampaignId);
        (await satellite.PostAsync($"{ConcessionsRoute}/{first.ConcessionId}/cancel", null)).EnsureSuccessStatusCode();
        var afterCancel = await CampaignStatusAsync(_factory, scenario.CampaignId);

        Assert.Equal("SoldOut", soldOut);
        Assert.Equal("Active", afterCancel);
        var refused = await QueryAsync(_factory, context => context.AuditEntries.SingleAsync(entry => entry.EventType == AuditEventTypes.VoucherIssuanceRefused && entry.EntityId == second.ConcessionId));
        Assert.Equal(scenario.CampaignId, refused.CampaignId);
        Assert.Contains("CampaignSoldOut", refused.Payload);
    }

    [Fact]
    public async Task Grant_WhenTheTargetQuotaIsReached_RefusesWithoutUsingAnotherTarget()
    {
        var scenario = await ApprovedCampaignAsync(_factory, (analista, id) =>
            analista.PutAsJsonAsync($"{ReadyCampaign.Route}/{id}/items", new
            {
                items = new object[]
                {
                    new { targetType = "Category", categoryId = DemoDataSeeder.FashionCategoryId, benefitType = "Percentage", benefitValue = 10, voucherLimit = 1 },
                    new { targetType = "All", benefitType = "FixedAmount", benefitValue = 5, voucherLimit = 50 }
                }
            }));
        var satellite = Satellite(_factory);

        var first = await GrantedAsync(satellite, Purchase(scenario.StoreId));
        await NotGrantedAsync(satellite, Purchase(scenario.StoreId), "TargetSoldOut");
        var other = await GrantedAsync(satellite, Purchase(scenario.StoreId, items: [new { productCode = "SEM-CATEGORIA", quantity = 1, unitPrice = 200m }]));

        Assert.Equal("Category", first.Voucher!.Target.TargetType);
        Assert.Equal(DemoDataSeeder.FashionCategoryId, first.Voucher.Target.CategoryId);
        Assert.Equal("All", other.Voucher!.Target.TargetType);
        var stored = await StoredCampaignAsync(_factory, scenario.CampaignId);
        Assert.Equal(1, stored.Items.Single(item => item.TargetType == TargetType.Category).ReservedVouchers);
        Assert.Equal(1, stored.Items.Single(item => item.TargetType == TargetType.All).ReservedVouchers);
        Assert.Equal(2, stored.ReservedVouchers);
    }

    [Fact]
    public async Task Grant_WhenTheCustomerReachedTheLimit_IsRefusedForThatCustomerOnly()
    {
        var scenario = await ApprovedCampaignAsync(_factory, (analista, id) =>
            analista.PutAsJsonAsync($"{ReadyCampaign.Route}/{id}/quantities", new { totalVoucherLimit = 100, perCustomerVoucherLimit = 2 }));
        var satellite = Satellite(_factory);

        await GrantedAsync(satellite, Purchase(scenario.StoreId, document: "99988877766"));
        await GrantedAsync(satellite, Purchase(scenario.StoreId, document: "999.888.777-66"));
        await NotGrantedAsync(satellite, Purchase(scenario.StoreId, document: "99988877766"), "CustomerLimitReached");
        var other = await GrantedAsync(satellite, Purchase(scenario.StoreId, document: "11122233344"));

        Assert.NotNull(other.Voucher);
    }

    [Fact]
    public async Task Grant_OutsideTheScopeOrChannel_IsRefused()
    {
        var scenario = await ApprovedCampaignAsync(_factory);
        var otherStore = await NewStoreAsync(_factory, "Physical", "Virtual");
        var inactiveStore = await NewStoreAsync(_factory);
        (await User(_factory, Roles.Administrador).DeleteAsync($"/api/v1/establishments/{inactiveStore}")).EnsureSuccessStatusCode();
        var satellite = Satellite(_factory);

        await NotGrantedAsync(satellite, Purchase(otherStore), "NoCampaignMatched");
        var virtualAtPhysical = await satellite.PostAsJsonAsync(ConcessionsRoute, Purchase(scenario.StoreId, channel: "Virtual"));
        var unknown = await satellite.PostAsJsonAsync(ConcessionsRoute, Purchase(Guid.CreateVersion7()));
        var inactive = await satellite.PostAsJsonAsync(ConcessionsRoute, Purchase(inactiveStore));
        var inScope = await GrantedAsync(satellite, Purchase(scenario.StoreId));

        var problem = await AssertProblemAsync(virtualAtPhysical, HttpStatusCode.UnprocessableEntity, ConcessionErrors.ChannelNotEnabledCode);
        Assert.EndsWith("não opera o canal virtual.", problem.GetProperty("detail").GetString());
        await AssertProblemAsync(unknown, HttpStatusCode.UnprocessableEntity, ConcessionErrors.EstablishmentNotFoundCode);
        await AssertProblemAsync(inactive, HttpStatusCode.UnprocessableEntity, ConcessionErrors.EstablishmentInactiveCode);
        Assert.NotNull(inScope.Voucher);
        Assert.Equal(0, await QueryAsync(_factory, context => context.Concessions.CountAsync(concession => concession.EstablishmentId == inactiveStore)));
    }

    [Fact]
    public async Task Grant_ReleasesAnExpiredReservationOfTheSameCampaignFirst()
    {
        var scenario = await ApprovedCampaignAsync(_factory, (analista, id) =>
            analista.PutAsJsonAsync($"{ReadyCampaign.Route}/{id}/quantities", new { totalVoucherLimit = 1 }));
        var satellite = Satellite(_factory);

        var first = await GrantedAsync(satellite, Purchase(scenario.StoreId));
        _factory.Clock.UtcNow = Now.AddMinutes(16);
        var second = await GrantedAsync(satellite, Purchase(scenario.StoreId));
        var firstState = await satellite.GetFromJsonAsync<ConcessionResponseDto>($"{ConcessionsRoute}/{first.ConcessionId}");
        _factory.Clock.UtcNow = Now;

        Assert.NotEqual(first.Voucher!.Code, second.Voucher!.Code);
        Assert.Equal("Expired", firstState!.Status);
        Assert.Equal(1, (await StoredCampaignAsync(_factory, scenario.CampaignId)).ReservedVouchers);
    }

    [Fact]
    public async Task Grant_EvaluatesTheConfiguredConditions()
    {
        var scenario = await ApprovedCampaignAsync(_factory, (analista, id) =>
            analista.PutAsJsonAsync($"{ReadyCampaign.Route}/{id}/conditions", new
            {
                conditions = new object[]
                {
                    new { type = "Category", categoryId = DemoDataSeeder.FashionCategoryId },
                    new { type = "CustomerPurchaseCount", minQuantity = 3 }
                },
                allowUseInSamePurchase = true
            }));
        var satellite = Satellite(_factory);
        object Fashion(int? purchases, string code = "mod-001") =>
            Purchase(scenario.StoreId, items: [new { productCode = code, quantity = 1, unitPrice = 200m }], customerPurchaseCount: purchases);

        var granted = await GrantedAsync(satellite, Fashion(3));
        await NotGrantedAsync(satellite, Fashion(2), "NoCampaignMatched");
        await NotGrantedAsync(satellite, Fashion(null), "NoCampaignMatched");
        await NotGrantedAsync(satellite, Fashion(3, "SEM-CADASTRO"), "NoCampaignMatched");

        Assert.True(granted.Voucher!.AllowUseInSamePurchase);
    }

    // D1: 10% sobre R$ 200 (R$ 20) vence R$ 15 fixos; uma compra gera um único voucher.
    [Fact]
    public async Task Grant_WithTwoSatisfiedCampaigns_ChoosesTheBiggerBenefitOnly()
    {
        _factory.Clock.UtcNow = Setup;
        var storeId = await NewStoreAsync(_factory);
        async Task<Guid> CampaignAsync(object item)
        {
            var analista = User(_factory);
            var id = await ReadyCampaign.CreateAsync(analista);
            (await analista.PutAsJsonAsync($"{ReadyCampaign.Route}/{id}/items", new { items = new[] { item } })).EnsureSuccessStatusCode();
            (await analista.PutAsJsonAsync($"{ReadyCampaign.Route}/{id}/scopes", new { scopes = new[] { new { establishmentId = storeId, channels = new[] { "Physical" } } } })).EnsureSuccessStatusCode();
            (await analista.PostAsync($"{ReadyCampaign.Route}/{id}/submit", null)).EnsureSuccessStatusCode();
            (await User(_factory, Roles.Gestor).PostAsync($"{ReadyCampaign.Route}/{id}/approve", null)).EnsureSuccessStatusCode();
            return id;
        }
        var fixedAmount = await CampaignAsync(new { targetType = "All", benefitType = "FixedAmount", benefitValue = 15 });
        var percentage = await CampaignAsync(new { targetType = "All", benefitType = "Percentage", benefitValue = 10 });
        _factory.Clock.UtcNow = Now;

        var granted = await GrantedAsync(Satellite(_factory), Purchase(storeId));

        Assert.Equal(percentage, granted.Voucher!.CampaignId);
        Assert.Equal(0, (await StoredCampaignAsync(_factory, fixedAmount)).ReservedVouchers);
    }

    [Fact]
    public async Task IssuedVoucher_SurvivesTheEndOfTheCampaign()
    {
        var scenario = await ApprovedCampaignAsync(_factory);
        var satellite = Satellite(_factory);
        var granted = await GrantedAsync(satellite, Purchase(scenario.StoreId, document: "55566677788"));
        (await satellite.PostAsync($"{ConcessionsRoute}/{granted.ConcessionId}/confirm", null)).EnsureSuccessStatusCode();

        (await User(_factory).PostAsync($"{ReadyCampaign.Route}/{scenario.CampaignId}/end", null)).EnsureSuccessStatusCode();
        var vouchers = await satellite.GetFromJsonAsync<List<VoucherResponseDto>>("/api/v1/satellite/customers/55566677788/vouchers");
        await NotGrantedAsync(satellite, Purchase(scenario.StoreId, document: "55566677788"), "CampaignNotActive");

        Assert.Equal("Available", Assert.Single(vouchers!, voucher => voucher.Id == granted.Voucher!.Id).Status);
    }

    [Theory]
    [InlineData("""{"customer":{"document":"12345678901"},"establishmentId":"0199e3a0-0000-7000-8000-000000000201","channel":"Physical","totalAmount":10,"items":[{"productCode":"A","quantity":1,"unitPrice":10}]}""", "TransactionId")]
    [InlineData("""{"transactionId":"T","customer":{"document":"1234567890"},"establishmentId":"0199e3a0-0000-7000-8000-000000000201","channel":"Physical","totalAmount":10,"items":[{"productCode":"A","quantity":1,"unitPrice":10}]}""", "Customer.Document")]
    [InlineData("""{"transactionId":"T","customer":{"document":"12345678901"},"establishmentId":"0199e3a0-0000-7000-8000-000000000201","channel":"Loja","totalAmount":10,"items":[{"productCode":"A","quantity":1,"unitPrice":10}]}""", "Channel")]
    [InlineData("""{"transactionId":"T","customer":{"document":"12345678901"},"establishmentId":"0199e3a0-0000-7000-8000-000000000201","channel":"Physical","totalAmount":0,"items":[{"productCode":"A","quantity":1,"unitPrice":10}]}""", "TotalAmount")]
    [InlineData("""{"transactionId":"T","customer":{"document":"12345678901"},"establishmentId":"0199e3a0-0000-7000-8000-000000000201","channel":"Physical","totalAmount":10.123,"items":[{"productCode":"A","quantity":1,"unitPrice":10}]}""", "TotalAmount")]
    [InlineData("""{"transactionId":"T","customer":{"document":"12345678901"},"establishmentId":"0199e3a0-0000-7000-8000-000000000201","channel":"Physical","totalAmount":10,"items":[]}""", "Items")]
    [InlineData("""{"transactionId":"T","customer":{"document":"12345678901"},"establishmentId":"0199e3a0-0000-7000-8000-000000000201","channel":"Physical","totalAmount":10,"items":[{"productCode":"A","quantity":0,"unitPrice":10}]}""", "Items[0].Quantity")]
    [InlineData("""{"transactionId":"T","customer":{"document":"12345678901"},"establishmentId":"0199e3a0-0000-7000-8000-000000000201","channel":"Physical","totalAmount":10,"items":[{"productCode":"A","quantity":1,"unitPrice":-1}]}""", "Items[0].UnitPrice")]
    public async Task Grant_WithInvalidContract_ReturnsValidationProblem(string json, string field)
    {
        var response = await Satellite(_factory).PostAsync(ConcessionsRoute, new StringContent(json, Encoding.UTF8, "application/json"));

        var problem = await AssertProblemAsync(response, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        Assert.True(problem.GetProperty("errors").TryGetProperty(field, out _), $"campo esperado: {field}");
    }

    [Fact]
    public async Task Routes_RequireASatelliteTokenWithTheIssueScope()
    {
        var scenario = await ApprovedCampaignAsync(_factory);
        var granted = await GrantedAsync(Satellite(_factory), Purchase(scenario.StoreId));

        var anonymous = await _factory.CreateClient().PostAsJsonAsync(ConcessionsRoute, Purchase(scenario.StoreId));
        var userToken = await User(_factory).PostAsJsonAsync(ConcessionsRoute, Purchase(scenario.StoreId));
        var noScope = await Satellite(_factory, scopes: [SatelliteScopes.ReadCampaigns]).PostAsJsonAsync(ConcessionsRoute, Purchase(scenario.StoreId));
        var otherNetwork = await Satellite(_factory, networkId: TestAuth.OtherNetworkId).PostAsync($"{ConcessionsRoute}/{granted.ConcessionId}/confirm", null);
        var otherNetworkStore = await Satellite(_factory, networkId: TestAuth.OtherNetworkId).PostAsJsonAsync(ConcessionsRoute, Purchase(scenario.StoreId));

        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, userToken.StatusCode);
        await AssertProblemAsync(noScope, HttpStatusCode.Forbidden, "AUTH_FORBIDDEN_SCOPE");
        Assert.Equal(HttpStatusCode.NotFound, otherNetwork.StatusCode);
        await AssertProblemAsync(otherNetworkStore, HttpStatusCode.UnprocessableEntity, ConcessionErrors.EstablishmentNotFoundCode);
    }

    [Fact]
    public async Task ConfirmAndCancel_OfANotGrantedConcession_AreRefused()
    {
        var storeId = await NewStoreAsync(_factory);
        var satellite = Satellite(_factory);
        var refused = await NotGrantedAsync(satellite, Purchase(storeId), "NoCampaignMatched");

        var confirm = await satellite.PostAsync($"{ConcessionsRoute}/{refused.ConcessionId}/confirm", null);
        var cancel = await satellite.PostAsync($"{ConcessionsRoute}/{refused.ConcessionId}/cancel", null);
        var missing = await satellite.PostAsync($"{ConcessionsRoute}/{Guid.CreateVersion7()}/confirm", null);

        await AssertProblemAsync(confirm, HttpStatusCode.UnprocessableEntity, ConcessionErrors.NotGrantedCode);
        await AssertProblemAsync(cancel, HttpStatusCode.UnprocessableEntity, ConcessionErrors.NotGrantedCode);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task VoucherEvents_AreAppendOnly()
    {
        var scenario = await ApprovedCampaignAsync(_factory);
        var granted = await GrantedAsync(Satellite(_factory), Purchase(scenario.StoreId));

        await using var scope = _factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var stored = await context.VoucherEvents.SingleAsync(item => item.VoucherId == granted.Voucher!.Id);
        stored.EventType = "Hacked";

        await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
    }

    private static async Task<JsonElement> AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        var problem = await TestAuth.ReadProblemAsync(response);
        Assert.Equal(code, problem.GetProperty("code").GetString());

        return problem;
    }
}
