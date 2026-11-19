using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VoucherCampaigns.Application.Audit;
using VoucherCampaigns.Application.Authentication;
using VoucherCampaigns.Application.Campaigns;
using VoucherCampaigns.Domain.Audit;
using VoucherCampaigns.Infrastructure.Persistence.Seed;
using VoucherCampaigns.IntegrationTests.Authentication;
using VoucherCampaigns.IntegrationTests.Campaigns;
using static VoucherCampaigns.IntegrationTests.Vouchers.MotorScenario;

namespace VoucherCampaigns.IntegrationTests.Vouchers;

/// <summary>
/// Restrições no resgate (spec GH-070): alvos de resgate, janelas de dias e horários e as duas regras que já existiam
/// (CPF titular e mínimo de resgate separado do de concessão). A rede de demonstração está em America/Sao_Paulo; a
/// concessão acontece no sábado 10/10/2026 12:00.
/// </summary>
public class RedemptionRestrictionsEndpointTests : IClassFixture<ApiFactory>
{
    private const string ProductsImportRoute = "/api/v1/products/import";
    private static readonly TimeSpan Brasilia = TimeSpan.FromHours(-3);
    private readonly ApiFactory _factory;

    /// <summary>CPF próprio de cada teste: os vouchers dos outros testes da mesma fábrica não entram na verificação.</summary>
    private readonly string _document = Random.Shared.NextInt64(10_000_000_000, 99_999_999_999).ToString();

    public RedemptionRestrictionsEndpointTests(ApiFactory factory)
    {
        _factory = factory;
        _factory.Clock.UtcNow = Now;
    }

    [Fact]
    public async Task RedemptionTargets_RequireTheirItemsInThePurchase_AndTheDiscountIsOnThoseItems()
    {
        // Concessão em qualquer item (alvo "todos" 10%); resgate só em calçados.
        var scenario = await ApprovedCampaignAsync(_factory, (analista, id) =>
            PutOkAsync(analista, TargetsRoute(id), Targets(new { targetType = "Category", categoryId = DemoDataSeeder.ShoesCategoryId })));
        var satellite = Satellite(_factory);
        var voucher = await AvailableVoucherAsync(satellite, scenario, _document);

        var onlyFashion = await CheckAsync(satellite, Purchase(scenario.StoreId, document: _document));
        var refused = Assert.Single(onlyFashion.NotApplicable);
        Assert.Equal("TargetNotInPurchase", refused.Reason);

        var (response, _) = await RedeemAsync(satellite, Redeem(voucher.Id, scenario.StoreId, document: _document));
        Assert.Equal("REDEMPTION_TARGET_NOT_IN_PURCHASE", (await TestAuth.ReadProblemAsync(response)).GetProperty("code").GetString());

        var mixed = new object[]
        {
            new { productCode = "MOD-001", quantity = 1, unitPrice = 200m },
            new { productCode = "CAL-001", quantity = 1, unitPrice = 300m }
        };
        var redeemed = await RedeemedAsync(satellite, Redeem(voucher.Id, scenario.StoreId, document: _document, totalAmount: 500m, items: mixed));

        // 10% sobre os R$ 300 dos calçados, não sobre os R$ 500 da compra.
        Assert.Equal(30.00m, redeemed.DiscountAmount);
    }

    [Fact]
    public async Task RedemptionTargets_FixedAmount_IsCappedByTheBase_AndAnItemMatchingTwoTargetsCountsOnce()
    {
        var scenario = await ApprovedCampaignAsync(_factory, async (analista, id) =>
        {
            await PutOkAsync(analista, $"{ReadyCampaign.Route}/{id}/items", new
            {
                items = new[] { new { targetType = "All", benefitType = "FixedAmount", benefitValue = 150 } }
            });
            await PutOkAsync(analista, TargetsRoute(id), Targets(
                new { targetType = "Category", categoryId = DemoDataSeeder.ShoesCategoryId },
                new { targetType = "Product", productCode = "cal-001" }));
        });
        var satellite = Satellite(_factory);
        var voucher = await AvailableVoucherAsync(satellite, scenario, _document);
        var items = new object[]
        {
            new { productCode = "CAL-001", quantity = 1, unitPrice = 100m },
            new { productCode = "MOD-001", quantity = 1, unitPrice = 200m }
        };

        var redeemed = await RedeemedAsync(satellite, Redeem(voucher.Id, scenario.StoreId, document: _document, totalAmount: 300m, items: items));

        // A base é o tênis (R$ 100), contado uma vez mesmo casando com a categoria e com o produto.
        Assert.Equal(100.00m, redeemed.DiscountAmount);
    }

    [Fact]
    public async Task RedemptionWindows_UseTheNetworkTime_AndTheEndMinuteCountsWhole()
    {
        var scenario = await ApprovedCampaignAsync(_factory, (analista, id) =>
            PutOkAsync(analista, WindowsRoute(id), Windows(new
            {
                daysOfWeek = new[] { "Monday", "Tuesday", "Wednesday", "Thursday", "Friday" },
                start = "08:00",
                end = "11:59"
            })));
        var satellite = Satellite(_factory);
        var voucher = await AvailableVoucherAsync(satellite, scenario, _document);

        // Sábado 12:00, a hora da concessão: fora da janela.
        var saturday = Assert.Single((await CheckAsync(satellite, Purchase(scenario.StoreId, document: _document))).NotApplicable);
        Assert.Equal("OutsideRedemptionWindow", saturday.Reason);
        Assert.Equal("O voucher não pode ser usado neste dia e horário.", saturday.Detail);

        Assert.Equal("OutsideRedemptionWindow", await ReasonAtAsync(satellite, scenario, new DateTimeOffset(2026, 10, 16, 12, 0, 0, Brasilia)));
        Assert.Equal("OutsideRedemptionWindow", await ReasonAtAsync(satellite, scenario, new DateTimeOffset(2026, 10, 16, 7, 59, 59, Brasilia)));
        Assert.Null(await ReasonAtAsync(satellite, scenario, new DateTimeOffset(2026, 10, 16, 8, 0, 0, Brasilia)));

        _factory.Clock.UtcNow = new DateTimeOffset(2026, 10, 16, 11, 59, 59, Brasilia);
        var (redeemedAt, _) = await RedeemAsync(satellite, Redeem(voucher.Id, scenario.StoreId, document: _document));
        Assert.True(redeemedAt.IsSuccessStatusCode, await redeemedAt.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task RedemptionWindows_ThatCrossMidnight_BelongToTheStartDay_AndTheDayIsTheLocalOne()
    {
        var scenario = await ApprovedCampaignAsync(_factory, (analista, id) =>
            PutOkAsync(analista, WindowsRoute(id), Windows(
                new { daysOfWeek = new[] { "Friday" }, start = "22:00", end = "01:59" },
                new { daysOfWeek = new[] { "Sunday" }, start = "21:00", end = "23:00" })));
        var satellite = Satellite(_factory);
        await AvailableVoucherAsync(satellite, scenario, _document);

        // Sexta 22:00 a sábado 01:59.
        Assert.Null(await ReasonAtAsync(satellite, scenario, new DateTimeOffset(2026, 10, 16, 23, 0, 0, Brasilia)));
        Assert.Null(await ReasonAtAsync(satellite, scenario, new DateTimeOffset(2026, 10, 17, 1, 30, 0, Brasilia)));
        Assert.Equal("OutsideRedemptionWindow", await ReasonAtAsync(satellite, scenario, new DateTimeOffset(2026, 10, 16, 1, 30, 0, Brasilia)));
        Assert.Equal("OutsideRedemptionWindow", await ReasonAtAsync(satellite, scenario, new DateTimeOffset(2026, 10, 17, 2, 0, 0, Brasilia)));

        // Domingo 22:00 em Brasília já é segunda 01:00 em UTC: vale o dia local.
        Assert.Null(await ReasonAtAsync(satellite, scenario, new DateTimeOffset(2026, 10, 18, 22, 0, 0, Brasilia)));
    }

    [Fact]
    public async Task OutOfScope_ComesBeforeTheWindow_AndTheWindowBeforeTheMinimumAmount()
    {
        var scenario = await ApprovedCampaignAsync(_factory, async (analista, id) =>
        {
            await PutOkAsync(analista, WindowsRoute(id), Windows(new { daysOfWeek = new[] { "Monday" }, start = "08:00", end = "09:00" }));
            await PutOkAsync(analista, $"{ReadyCampaign.Route}/{id}/usage-restrictions", new { minPurchaseAmount = 1000, maxUsesPerVoucher = 1 });
        });
        var satellite = Satellite(_factory);
        await AvailableVoucherAsync(satellite, scenario, _document);
        var otherStore = await NewStoreAsync(_factory);

        var outOfScope = Assert.Single((await CheckAsync(satellite, Purchase(otherStore, document: _document))).NotApplicable);
        var outsideWindow = Assert.Single((await CheckAsync(satellite, Purchase(scenario.StoreId, document: _document))).NotApplicable);

        Assert.Equal("OutOfScope", outOfScope.Reason);
        Assert.Equal("OutsideRedemptionWindow", outsideWindow.Reason);
        Assert.Equal("AmountBelowMinimum", await ReasonAtAsync(satellite, scenario, new DateTimeOffset(2026, 10, 12, 8, 30, 0, Brasilia)));
    }

    [Fact]
    public async Task RedemptionMinimum_IsSeparateFromTheConcessionMinimum()
    {
        // D-1: a condição PurchaseTotal (concessão) e o minPurchaseAmount (resgate) já eram campos separados.
        var scenario = await ApprovedCampaignAsync(_factory, async (analista, id) =>
        {
            await PutOkAsync(analista, $"{ReadyCampaign.Route}/{id}/conditions", new
            {
                conditions = new[] { new { type = "PurchaseTotal", minAmount = 100 } },
                allowUseInSamePurchase = false
            });
            await PutOkAsync(analista, $"{ReadyCampaign.Route}/{id}/usage-restrictions", new { minPurchaseAmount = 300, maxUsesPerVoucher = 1 });
        });
        var satellite = Satellite(_factory);
        var cheap = new object[] { new { productCode = "MOD-001", quantity = 1, unitPrice = 150m } };
        var granted = await GrantedAsync(satellite, Purchase(scenario.StoreId, document: _document, totalAmount: 150m, items: cheap));
        var voucher = (await ConfirmedAsync(satellite, granted.ConcessionId)).Voucher!;

        var (below, _) = await RedeemAsync(satellite, Redeem(voucher.Id, scenario.StoreId, document: _document, totalAmount: 150m, items: cheap));
        Assert.Equal("REDEMPTION_AMOUNT_BELOW_MINIMUM", (await TestAuth.ReadProblemAsync(below)).GetProperty("code").GetString());

        var expensive = new object[] { new { productCode = "MOD-001", quantity = 1, unitPrice = 300m } };
        await RedeemedAsync(satellite, Redeem(voucher.Id, scenario.StoreId, document: _document, totalAmount: 300m, items: expensive));
    }

    [Fact]
    public async Task RestrictionsAppearInThePortalAndSatellite_AreAudited_AndCopiedByDuplicate()
    {
        var scenario = await ApprovedCampaignAsync(_factory, async (analista, id) =>
        {
            await PutOkAsync(analista, TargetsRoute(id), Targets(
                new { targetType = "Product", productCode = "cal-001" },
                new { targetType = "Category", categoryId = DemoDataSeeder.ShoesCategoryId }));
            await PutOkAsync(analista, WindowsRoute(id), Windows(new { daysOfWeek = new[] { "Sunday", "Monday" }, start = "08:00", end = "18:30" }));

            // A mesma lista de novo não grava trilha.
            await PutOkAsync(analista, WindowsRoute(id), Windows(new { daysOfWeek = new[] { "Monday", "Sunday" }, start = "08:00", end = "18:30" }));
        });

        var campaign = (await User(_factory).GetFromJsonAsync<CampaignResponse>($"{ReadyCampaign.Route}/{scenario.CampaignId}"))!;
        Assert.Equal(["Product", "Category"], campaign.RedemptionTargets!.Select(target => target.TargetType));
        Assert.Equal("CAL-001", campaign.RedemptionTargets![0].ProductCode);
        var window = Assert.Single(campaign.RedemptionWindows!);
        Assert.Equal(["Monday", "Sunday"], window.DaysOfWeek);
        Assert.Equal(("08:00", "18:30"), (window.Start, window.End));

        using var satelliteCampaigns = JsonDocument.Parse(await Satellite(_factory).GetStringAsync($"{SatelliteCampaignsRoute}?establishmentId={scenario.StoreId}"));
        var listed = satelliteCampaigns.RootElement.EnumerateArray().Single(item => item.GetProperty("id").GetGuid() == scenario.CampaignId);
        Assert.Equal(2, listed.GetProperty("redemptionTargets").GetArrayLength());
        Assert.Equal("18:30", listed.GetProperty("redemptionWindows")[0].GetProperty("end").GetString());

        var sections = await QueryAsync(_factory, db => db.AuditEntries
            .Where(entry => entry.CampaignId == scenario.CampaignId && entry.EventType == AuditEventTypes.CampaignUpdated)
            .Select(entry => entry.Payload!)
            .ToListAsync());
        Assert.Single(sections, payload => payload.Contains("\"section\":\"redemptionTargets\""));
        Assert.Single(sections, payload => payload.Contains("\"section\":\"redemptionWindows\""));

        var duplicate = await User(_factory).PostAsJsonAsync($"{ReadyCampaign.Route}/{scenario.CampaignId}/duplicate", new { startsAt = "2026-11-02T08:00:00", endsAt = "2026-11-30T23:59:00" });
        duplicate.EnsureSuccessStatusCode();
        var copy = (await duplicate.Content.ReadFromJsonAsync<CampaignResponse>())!;
        Assert.Equal(campaign.RedemptionTargets!.Select(target => (target.TargetType, target.ProductCode)), copy.RedemptionTargets!.Select(target => (target.TargetType, target.ProductCode)));
        var copied = Assert.Single(copy.RedemptionWindows!);
        Assert.Equal(window.DaysOfWeek, copied.DaysOfWeek);
        Assert.Equal((window.Start, window.End), (copied.Start, copied.End));

        // Fora de rascunho, nada muda.
        var locked = await User(_factory).PutAsJsonAsync(WindowsRoute(scenario.CampaignId), Windows());
        Assert.Equal("CAMPAIGN_NOT_DRAFT", (await TestAuth.ReadProblemAsync(locked)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task RedemptionTargets_Validation()
    {
        _factory.Clock.UtcNow = Setup;
        var analista = User(_factory);
        var id = await ReadyCampaign.CreateAsync(analista);
        var route = TargetsRoute(id);

        await AssertValidationAsync(analista, route, Targets(new { targetType = "All" }), "Targets[0].TargetType");
        await AssertValidationAsync(analista, route, Targets(new { targetType = "Product", productId = Guid.NewGuid(), productCode = "MOD-001" }), "Targets[0].ProductCode");
        await AssertValidationAsync(analista, route, Targets(new { targetType = "Category" }), "Targets[0].CategoryId");
        await AssertValidationAsync(analista, route, Targets(new { targetType = "Category", categoryId = DemoDataSeeder.ShoesCategoryId, productCode = "MOD-001" }), "Targets[0].ProductCode");
        await AssertValidationAsync(analista, route, new { targets = Enumerable.Range(0, CampaignContract.MaxItems + 1).Select(_ => new { targetType = "Product", productCode = "MOD-001" }).ToArray() }, "Targets");
        await AssertValidationAsync(analista, route, new { }, "Targets");

        var missing = await analista.PutAsJsonAsync(route, Targets(new { targetType = "Product", productCode = "NAO-EXISTE" }));
        var missingProblem = await TestAuth.ReadProblemAsync(missing);
        Assert.Equal("CAMPAIGN_ITEM_PRODUCT_CODE_NOT_FOUND", missingProblem.GetProperty("code").GetString());
        Assert.Equal(["NAO-EXISTE"], missingProblem.GetProperty("codes").EnumerateArray().Select(code => code.GetString()));

        var stored = await QueryAsync(_factory, db => db.Products.SingleAsync(product => product.Code == "MOD-001"));
        var duplicated = await analista.PutAsJsonAsync(route, Targets(
            new { targetType = "Product", productId = stored.Id },
            new { targetType = "Product", productCode = "mod-001" }));
        Assert.Equal("CAMPAIGN_REDEMPTION_TARGET_DUPLICATED", (await TestAuth.ReadProblemAsync(duplicated)).GetProperty("code").GetString());

        var unknownCategory = await analista.PutAsJsonAsync(route, Targets(new { targetType = "Category", categoryId = Guid.NewGuid() }));
        Assert.Equal("CAMPAIGN_ITEM_CATEGORY_NOT_FOUND", (await TestAuth.ReadProblemAsync(unknownCategory)).GetProperty("code").GetString());

        // Lista vazia volta ao alvo da concessão.
        await PutOkAsync(analista, route, Targets(new { targetType = "Category", categoryId = DemoDataSeeder.ShoesCategoryId }));
        var cleared = await PutOkAsync(analista, route, Targets());
        Assert.Empty(cleared.RedemptionTargets!);
    }

    [Fact]
    public async Task RedemptionWindows_Validation()
    {
        _factory.Clock.UtcNow = Setup;
        var analista = User(_factory);
        var id = await ReadyCampaign.CreateAsync(analista);
        var route = WindowsRoute(id);

        await AssertValidationAsync(analista, route, Windows(new { daysOfWeek = Array.Empty<string>(), start = "08:00", end = "09:00" }), "Windows[0].DaysOfWeek");
        await AssertValidationAsync(analista, route, Windows(new { daysOfWeek = new[] { "Segunda" }, start = "08:00", end = "09:00" }), "Windows[0].DaysOfWeek");
        await AssertValidationAsync(analista, route, Windows(new { daysOfWeek = new[] { "Monday", "monday" }, start = "08:00", end = "09:00" }), "Windows[0].DaysOfWeek");
        await AssertValidationAsync(analista, route, Windows(new { daysOfWeek = new[] { "Monday" }, start = "24:00", end = "09:00" }), "Windows[0].Start");
        await AssertValidationAsync(analista, route, Windows(new { daysOfWeek = new[] { "Monday" }, start = "08:00", end = "9h" }), "Windows[0].End");
        await AssertValidationAsync(analista, route, new
        {
            windows = Enumerable.Range(0, CampaignContract.MaxRedemptionWindows + 1).Select(_ => new { daysOfWeek = new[] { "Monday" }, start = "08:00", end = "09:00" }).ToArray()
        }, "Windows");

        var stored = await PutOkAsync(analista, route, Windows(new { daysOfWeek = new[] { "saturday" }, start = "10:00", end = "10:00" }));
        var saturday = Assert.Single(stored.RedemptionWindows!);
        Assert.Equal(["Saturday"], saturday.DaysOfWeek);
        Assert.Equal(("10:00", "10:00"), (saturday.Start, saturday.End));
    }

    [Fact]
    public async Task RedemptionTargets_WithAThousandImportedCodes_AreAuditedAsASummary()
    {
        var prefix = $"R{Guid.NewGuid():N}"[..7].ToUpperInvariant();
        var codes = Enumerable.Range(1, CampaignContract.MaxItems).Select(index => $"{prefix}{index:D4}").ToArray();
        var admin = _factory.CreateClient().WithBearer(TestAuth.CreateToken(_factory, Roles.Administrador, null));
        (await admin.PostAsJsonAsync(ProductsImportRoute, new
        {
            categoryId = DemoDataSeeder.ShoesCategoryId,
            products = codes.Select(code => new { code, name = $"Produto {code}" }).ToArray()
        })).EnsureSuccessStatusCode();
        _factory.Clock.UtcNow = Setup;
        var analista = User(_factory);
        var id = await ReadyCampaign.CreateAsync(analista);

        var body = await PutOkAsync(analista, TargetsRoute(id), new { targets = codes.Select(code => new { targetType = "Product", productCode = code }).ToArray() });

        Assert.Equal(codes, body.RedemptionTargets!.Select(target => target.ProductCode));
        var payload = await QueryAsync(_factory, db => db.AuditEntries
            .Where(entry => entry.CampaignId == id && entry.EventType == AuditEventTypes.CampaignUpdated)
            .OrderByDescending(entry => entry.Id)
            .Select(entry => entry.Payload)
            .FirstAsync());
        Assert.True(payload!.Length <= AuditJson.MaxPayloadLength, payload);
        using var json = JsonDocument.Parse(payload);
        var after = json.RootElement.GetProperty("after");
        Assert.Equal("redemptionTargets", json.RootElement.GetProperty("section").GetString());
        Assert.Equal(CampaignContract.MaxItems, after.GetProperty("count").GetInt32());
        Assert.Equal(CampaignContract.MaxItems, after.GetProperty("targets").GetProperty("Product").GetInt32());
        Assert.Equal(CampaignContract.MaxItems, after.GetProperty("changes").GetProperty("added").GetInt32());
    }

    /// <summary>Motivo do voucher do cliente na verificação (sem consumir utilização) com o relógio no instante dado; null = aplicável.</summary>
    private async Task<string?> ReasonAtAsync(HttpClient satellite, Scenario scenario, DateTimeOffset at)
    {
        _factory.Clock.UtcNow = at;
        var check = await CheckAsync(satellite, Purchase(scenario.StoreId, document: _document));
        _factory.Clock.UtcNow = Now;

        return check.Applicable.Count > 0 ? null : Assert.Single(check.NotApplicable).Reason;
    }

    private static string TargetsRoute(Guid id) => $"{ReadyCampaign.Route}/{id}/redemption-targets";

    private static string WindowsRoute(Guid id) => $"{ReadyCampaign.Route}/{id}/redemption-windows";

    private static object Targets(params object[] targets) => new { targets };

    private static object Windows(params object[] windows) => new { windows };

    private static async Task<CampaignResponse> PutOkAsync(HttpClient client, string route, object body)
    {
        var response = await client.PutAsJsonAsync(route, body);
        Assert.True(response.IsSuccessStatusCode, $"{route}: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");

        return (await response.Content.ReadFromJsonAsync<CampaignResponse>())!;
    }

    private static async Task AssertValidationAsync(HttpClient client, string route, object body, string field)
    {
        var response = await client.PutAsJsonAsync(route, body);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await TestAuth.ReadProblemAsync(response);
        Assert.Equal("VALIDATION_ERROR", problem.GetProperty("code").GetString());
        Assert.True(problem.GetProperty("errors").TryGetProperty(field, out _), $"campo {field}: {problem}");
    }
}
