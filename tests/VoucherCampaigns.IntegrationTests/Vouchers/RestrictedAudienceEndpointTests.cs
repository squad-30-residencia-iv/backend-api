using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VoucherCampaigns.Application.Authentication;
using VoucherCampaigns.Application.Campaigns;
using VoucherCampaigns.Application.Eligibility;
using VoucherCampaigns.Domain.Audit;
using VoucherCampaigns.Infrastructure.Persistence.Seed;
using VoucherCampaigns.IntegrationTests.Campaigns;
using static VoucherCampaigns.IntegrationTests.Vouchers.MotorScenario;

namespace VoucherCampaigns.IntegrationTests.Vouchers;

/// <summary>Público restrito: configuração, avaliação na emissão, resgate, simulação e satélite (spec GH-047).</summary>
public class RestrictedAudienceEndpointTests : IClassFixture<ApiFactory>
{
    private static readonly object Loyalty = new { type = "LoyaltyMember" };

    private readonly ApiFactory _factory;

    /// <summary>CPF válido e próprio de cada teste.</summary>
    private readonly string _document = TestCpf.New();

    public RestrictedAudienceEndpointTests(ApiFactory factory)
    {
        _factory = factory;
        _factory.Clock.UtcNow = Now;
    }

    // ---------- Configuração (US-06.1 a 06.5, D-9 e D-10) ----------

    [Fact]
    public async Task PutAudience_Restricted_StoresTheCriteriaAndShowsTheListOnlyAsACount()
    {
        _factory.Clock.UtcNow = Setup;
        var analista = User(_factory);
        var id = await ReadyCampaign.CreateAsync(analista);
        var first = TestCpf.New();
        var second = TestCpf.New();

        var response = await PutAudienceAsync(analista, id, Loyalty,
            new { type = "AgeRange", minAge = 18, maxAge = 30 },
            new { type = "Location", states = new[] { "sp", "PR" }, cityCodes = new[] { "4106902" } },
            new { type = "TotalSpent", minAmount = 500m, windowDays = 90 },
            new { type = "SpecificCustomers", documents = new[] { Masked(first), second, first } });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = (await response.Content.ReadFromJsonAsync<CampaignResponse>())!;
        Assert.Equal("Restricted", body.AudienceType);
        Assert.Equal(["LoyaltyMember", "AgeRange", "Location", "TotalSpent", "SpecificCustomers"], body.Eligibility!.Criteria.Select(criterion => criterion.Type));
        Assert.Equal(["SP", "PR"], body.Eligibility.Criteria[2].States);
        Assert.Equal(2, body.Eligibility.Criteria[4].CustomerCount);

        var detail = await analista.GetFromJsonAsync<CampaignResponse>($"{ReadyCampaign.Route}/{id}");
        Assert.Equivalent(body.Eligibility, detail!.Eligibility);

        var page = await analista.GetFromJsonAsync<EligibleCustomersResponse>($"{ReadyCampaign.Route}/{id}/audience/customers?page=2&pageSize=1");
        Assert.Equal(2, page!.Total);
        Assert.Equal([new[] { first, second }.Order().Last()], page.Documents);
    }

    [Theory]
    [InlineData("""{"audienceType":"Restricted"}""", "Criteria", EligibilityContract.CriteriaRequired)]
    [InlineData("""{"audienceType":"AllCustomers","criteria":[{"type":"LoyaltyMember"}]}""", "Criteria", EligibilityContract.CriteriaNotAllowed)]
    [InlineData("""{"audienceType":"Restricted","criteria":[{"type":"AgeRange","minAge":40,"maxAge":30}]}""", "Criteria[0].MinAge", EligibilityContract.AgeRangeInverted)]
    [InlineData("""{"audienceType":"Restricted","criteria":[{"type":"TotalSpent","minAmount":100}]}""", "Criteria[0].WindowDays", EligibilityContract.HistoryWindowRequired)]
    [InlineData("""{"audienceType":"Restricted","criteria":[{"type":"TotalSpent","minAmount":-1,"windowDays":90}]}""", "Criteria[0].MinAmount", EligibilityContract.MinAmountPositive)]
    [InlineData("""{"audienceType":"Restricted","criteria":[{"type":"PurchaseFrequency","minPurchases":0,"windowDays":90}]}""", "Criteria[0].MinPurchases", EligibilityContract.MinPurchasesPositive)]
    [InlineData("""{"audienceType":"Restricted","criteria":[{"type":"PurchasedCategory","categoryId":"0199e3a0-0000-7000-8000-00000000ffff","windowDays":90}]}""", "Criteria[0].CategoryId", EligibilityContract.CategoryNotFound)]
    [InlineData("""{"audienceType":"Restricted","criteria":[{"type":"LoyaltyMember","minAge":18}]}""", "Criteria[0].MinAge", EligibilityContract.UnexpectedField)]
    [InlineData("""{"audienceType":"Restricted","criteria":[{"type":"Location","states":["XX"]}]}""", "Criteria[0].States", EligibilityContract.StateInvalid)]
    [InlineData("""{"audienceType":"Restricted","criteria":[{"type":"LoyaltyMember"},{"type":"LoyaltyMember"}]}""", "Criteria[1].Type", EligibilityContract.CriterionDuplicated)]
    [InlineData("""{"audienceType":"Restricted","criteria":[{"type":"Clube"}]}""", "Criteria[0].Type", EligibilityContract.CriterionTypeInvalid)]
    public async Task PutAudience_WithInvalidCriteria_ReturnsValidationProblem(string json, string field, string message)
    {
        _factory.Clock.UtcNow = Setup;
        var analista = User(_factory);
        var id = await ReadyCampaign.CreateAsync(analista);

        var response = await analista.PutAsync($"{ReadyCampaign.Route}/{id}/audience", new StringContent(json, System.Text.Encoding.UTF8, "application/json"));

        await AssertValidationAsync(response, field, message);
        Assert.Equal("AllCustomers", (await analista.GetFromJsonAsync<CampaignResponse>($"{ReadyCampaign.Route}/{id}"))!.AudienceType);
    }

    [Fact]
    public async Task PutAudience_InvalidDocuments_AreAllReportedWithTheirPositionsAndNothingIsSaved()
    {
        _factory.Clock.UtcNow = Setup;
        var analista = User(_factory);
        var id = await ReadyCampaign.CreateAsync(analista);

        var response = await PutAudienceAsync(analista, id,
            new { type = "SpecificCustomers", documents = new[] { TestCpf.New(), "123.456.789-01", TestCpf.New(), "abc", "11111111111" } });

        await AssertValidationAsync(response, "Criteria[0].Documents", string.Format(EligibilityContract.DocumentsInvalid, "2, 4, 5"));
        Assert.Equal(0, await QueryAsync(_factory, db => db.CampaignEligibleCustomers.CountAsync(customer => customer.CampaignId == id)));
    }

    [Fact]
    public async Task PutAudience_BackToAllCustomers_ClearsTheCriteriaAndTheList()
    {
        _factory.Clock.UtcNow = Setup;
        var analista = User(_factory);
        var id = await ReadyCampaign.CreateAsync(analista);
        (await PutAudienceAsync(analista, id, Loyalty, new { type = "SpecificCustomers", documents = new[] { TestCpf.New() } })).EnsureSuccessStatusCode();

        var response = await analista.PutAsJsonAsync($"{ReadyCampaign.Route}/{id}/audience", new { audienceType = "AllCustomers" });

        Assert.Null((await response.Content.ReadFromJsonAsync<CampaignResponse>())!.Eligibility);
        Assert.Equal(0, await QueryAsync(_factory, db => db.CampaignEligibilityCriteria.CountAsync(criterion => criterion.CampaignId == id)));
        Assert.Equal(0, await QueryAsync(_factory, db => db.CampaignEligibleCustomers.CountAsync(customer => customer.CampaignId == id)));
    }

    [Fact]
    public async Task PutAudience_IsAudited_WithTheListOnlyAsCounts_AndNotWhenRepeated()
    {
        _factory.Clock.UtcNow = Setup;
        var analista = User(_factory);
        var id = await ReadyCampaign.CreateAsync(analista);
        var document = TestCpf.New();
        object[] criteria = [Loyalty, new { type = "SpecificCustomers", documents = new[] { document } }];

        (await PutAudienceAsync(analista, id, criteria)).EnsureSuccessStatusCode();
        (await PutAudienceAsync(analista, id, criteria)).EnsureSuccessStatusCode();

        var edits = await QueryAsync(_factory, db => db.AuditEntries.AsNoTracking()
            .Where(entry => entry.CampaignId == id && entry.EventType == AuditEventTypes.CampaignUpdated && entry.Payload!.Contains("\"audience\""))
            .ToListAsync());
        var payload = JsonDocument.Parse(Assert.Single(edits, entry => entry.Payload!.Contains("Restricted")).Payload!).RootElement;

        Assert.Equal("AllCustomers", payload.GetProperty("before").GetProperty("audienceType").GetString());
        var list = payload.GetProperty("after").GetProperty("criteria")[1];
        Assert.Equal(1, list.GetProperty("customerCount").GetInt32());
        Assert.False(list.TryGetProperty("customersDigest", out _));
        var changes = payload.GetProperty("after").GetProperty("customerChanges");
        Assert.Equal(1, changes.GetProperty("added").GetInt32());
        Assert.Equal(0, changes.GetProperty("removed").GetInt32());
        Assert.DoesNotContain(document, payload.GetRawText());
    }

    [Fact]
    public async Task PutAudience_ListSwappedForAnotherOfTheSameSize_IsAuditedWithWhoEnteredAndLeft()
    {
        _factory.Clock.UtcNow = Setup;
        var analista = User(_factory);
        var id = await ReadyCampaign.CreateAsync(analista);
        var kept = TestCpf.New();
        var leaving = TestCpf.New();
        var entering = TestCpf.New();

        (await PutAudienceAsync(analista, id, new { type = "SpecificCustomers", documents = new[] { kept, leaving } })).EnsureSuccessStatusCode();
        (await PutAudienceAsync(analista, id, new { type = "SpecificCustomers", documents = new[] { kept, entering } })).EnsureSuccessStatusCode();

        var edits = await QueryAsync(_factory, db => db.AuditEntries.AsNoTracking()
            .Where(entry => entry.CampaignId == id && entry.EventType == AuditEventTypes.CampaignUpdated && entry.Payload!.Contains("\"audience\""))
            .OrderBy(entry => entry.Id)
            .ToListAsync());
        var payload = JsonDocument.Parse(edits.Last().Payload!).RootElement;

        // Mesma quantidade antes e depois: sem o resumo, só as entradas e saídas mostram a troca.
        Assert.Equal(2, payload.GetProperty("before").GetProperty("criteria")[0].GetProperty("customerCount").GetInt32());
        Assert.Equal(2, payload.GetProperty("after").GetProperty("criteria")[0].GetProperty("customerCount").GetInt32());
        var changes = payload.GetProperty("after").GetProperty("customerChanges");
        Assert.Equal(1, changes.GetProperty("added").GetInt32());
        Assert.Equal(1, changes.GetProperty("removed").GetInt32());
        Assert.False(payload.GetProperty("before").TryGetProperty("customerChanges", out _));
        Assert.DoesNotContain(kept, payload.GetRawText());
        Assert.DoesNotContain(entering, payload.GetRawText());
    }

    [Fact]
    public async Task Duplicate_CopiesTheCriteriaAndTheList()
    {
        _factory.Clock.UtcNow = Setup;
        var analista = User(_factory);
        var id = await ReadyCampaign.CreateAsync(analista);
        var document = TestCpf.New();
        (await PutAudienceAsync(analista, id, Loyalty, new { type = "SpecificCustomers", documents = new[] { document } })).EnsureSuccessStatusCode();

        var copy = await (await analista.PostAsJsonAsync($"{ReadyCampaign.Route}/{id}/duplicate", new { })).Content.ReadFromJsonAsync<CampaignResponse>();

        Assert.Equal("Restricted", copy!.AudienceType);
        Assert.Equal(["LoyaltyMember", "SpecificCustomers"], copy.Eligibility!.Criteria.Select(criterion => criterion.Type));
        var page = await analista.GetFromJsonAsync<EligibleCustomersResponse>($"{ReadyCampaign.Route}/{copy.Id}/audience/customers");
        Assert.Equal([document], page!.Documents);
    }

    [Fact]
    public async Task PutAudience_AfterApproval_IsRefusedAsNotDraft()
    {
        var scenario = await ApprovedCampaignAsync(_factory);

        var response = await PutAudienceAsync(User(_factory), scenario.CampaignId, Loyalty);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("CAMPAIGN_NOT_DRAFT", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
    }

    // ---------- Concessão (US-04.2, US-06.3 a 06.5) ----------

    [Fact]
    public async Task Grant_ToALoyaltyMember_IssuesAndRecordsTheAttributesUsed()
    {
        var scenario = await RestrictedCampaignAsync(Loyalty);
        var satellite = Satellite(_factory);

        var granted = await GrantedAsync(satellite, PurchaseFor(scenario, new { document = _document, loyaltyMember = true }));

        var snapshot = await SnapshotAsync(granted.ConcessionId);
        Assert.True(snapshot.GetProperty("eligible").GetBoolean());
        var criterion = Assert.Single(snapshot.GetProperty("criteria").EnumerateArray().ToList());
        Assert.Equal("LoyaltyMember", criterion.GetProperty("type").GetString());
        Assert.True(criterion.GetProperty("observed").GetProperty("loyaltyMember").GetBoolean());
        Assert.Equal("Met", criterion.GetProperty("outcome").GetString());

        var issued = await QueryAsync(_factory, db => db.AuditEntries.AsNoTracking()
            .SingleAsync(entry => entry.CampaignId == scenario.CampaignId && entry.EventType == AuditEventTypes.VoucherIssued));
        Assert.Contains("\"eligibility\"", issued.Payload);
    }

    [Theory]
    [InlineData(null, "Missing")]
    [InlineData(false, "NotMet")]
    public async Task Grant_ToANonMember_IsRefusedWithoutTouchingTheBalance(bool? loyaltyMember, string outcome)
    {
        var scenario = await RestrictedCampaignAsync(Loyalty);
        var satellite = Satellite(_factory);

        var refused = await NotGrantedAsync(satellite, PurchaseFor(scenario, new { document = _document, loyaltyMember }), "CustomerNotEligible");

        var campaign = await StoredCampaignAsync(_factory, scenario.CampaignId);
        Assert.Equal(0, campaign.ReservedVouchers);
        Assert.Equal(0, campaign.IssuedVouchers);
        Assert.Equal(outcome, (await SnapshotAsync(refused.ConcessionId)).GetProperty("criteria")[0].GetProperty("outcome").GetString());

        var audit = await QueryAsync(_factory, db => db.AuditEntries.AsNoTracking()
            .SingleAsync(entry => entry.CampaignId == scenario.CampaignId && entry.EventType == AuditEventTypes.VoucherIssuanceRefused));
        var payload = JsonDocument.Parse(audit.Payload!).RootElement;
        Assert.Equal("CustomerNotEligible", payload.GetProperty("reason").GetString());
        Assert.Equal("LoyaltyMember", payload.GetProperty("failedCriterion").GetString());
        Assert.Equal(outcome, payload.GetProperty("failedOutcome").GetString());
    }

    [Fact]
    public async Task Grant_AllCustomers_DoesNotEvaluateNorRecordEligibility()
    {
        var scenario = await ApprovedCampaignAsync(_factory);

        var granted = await GrantedAsync(Satellite(_factory), PurchaseFor(scenario, new { document = _document, loyaltyMember = false }));

        Assert.Null(await QueryAsync(_factory, db => db.Concessions.Where(item => item.Id == granted.ConcessionId).Select(item => item.Eligibility).SingleAsync()));
    }

    [Fact]
    public async Task Grant_CombinesCriteriaWithAnd_AndUsesOnlyTheCriterionWindow()
    {
        var scenario = await RestrictedCampaignAsync(Loyalty, new { type = "TotalSpent", minAmount = 500m, windowDays = 90 });
        var satellite = Satellite(_factory);

        // Perfil atendido, histórico só na janela de 30 dias: a de 90 não veio (D-4, D-5).
        var refused = await NotGrantedAsync(satellite, PurchaseFor(scenario, new
        {
            document = _document,
            loyaltyMember = true,
            history = new[] { new { windowDays = 30, totalSpent = 900m } }
        }), "CustomerNotEligible");
        var criteria = (await SnapshotAsync(refused.ConcessionId)).GetProperty("criteria");
        Assert.Equal("Met", criteria[0].GetProperty("outcome").GetString());
        Assert.Equal("Missing", criteria[1].GetProperty("outcome").GetString());

        await NotGrantedAsync(satellite, PurchaseFor(scenario, new
        {
            document = _document,
            loyaltyMember = true,
            history = new[] { new { windowDays = 90, totalSpent = 499.99m } }
        }), "CustomerNotEligible");

        await GrantedAsync(satellite, PurchaseFor(scenario, new
        {
            document = _document,
            loyaltyMember = true,
            history = new[] { new { windowDays = 30, totalSpent = 10m }, new { windowDays = 90, totalSpent = 500m } }
        }));
    }

    [Fact]
    public async Task Grant_AgeIsCountedInCompleteYearsOnThePurchaseDateOfTheNetwork()
    {
        var scenario = await RestrictedCampaignAsync(new { type = "AgeRange", minAge = 18 });
        var satellite = Satellite(_factory);

        // A compra é em 10/10/2026, em Brasília: quem nasceu em 10/10/2008 faz 18 no dia.
        await NotGrantedAsync(satellite, PurchaseFor(scenario, new { document = _document, birthDate = "2008-10-11" }), "CustomerNotEligible");
        await GrantedAsync(satellite, PurchaseFor(scenario, new { document = _document, birthDate = "2008-10-10" }));
    }

    [Fact]
    public async Task Grant_SpecificCustomers_OnlyTheListedReceive()
    {
        var scenario = await RestrictedCampaignAsync(new { type = "SpecificCustomers", documents = new[] { Masked(_document) } });
        var satellite = Satellite(_factory);

        await NotGrantedAsync(satellite, PurchaseFor(scenario, new { document = TestCpf.New() }), "CustomerNotEligible");
        await GrantedAsync(satellite, PurchaseFor(scenario, new { document = _document }));
    }

    [Fact]
    public async Task Grant_PurchasedCategory_UsesTheProductsBoughtInTheWindow()
    {
        var scenario = await RestrictedCampaignAsync(new { type = "PurchasedCategory", categoryId = DemoDataSeeder.ShoesCategoryId, windowDays = 60 });
        var satellite = Satellite(_factory);

        await NotGrantedAsync(satellite, PurchaseFor(scenario, new
        {
            document = _document,
            history = new[] { new { windowDays = 60, productCodes = new[] { "MOD-001", "NAO-EXISTE" } } }
        }), "CustomerNotEligible");
        await GrantedAsync(satellite, PurchaseFor(scenario, new
        {
            document = _document,
            history = new[] { new { windowDays = 60, productCodes = new[] { "cal-001" } } }
        }));
    }

    [Fact]
    public async Task Grant_NotEligibleForTheBestCampaign_ReceivesTheNextOne()
    {
        var open = await ApprovedCampaignAsync(_factory);
        var restricted = await ApprovedCampaignAsync(_factory, async (analista, id) =>
        {
            (await analista.PutAsJsonAsync($"{ReadyCampaign.Route}/{id}/items", new { items = new[] { new { targetType = "All", benefitType = "Percentage", benefitValue = 50 } } }))
                .EnsureSuccessStatusCode();
            (await analista.PutAsJsonAsync($"{ReadyCampaign.Route}/{id}/scopes", new { scopes = new[] { new { establishmentId = open.StoreId, channels = new[] { "Physical" } } } }))
                .EnsureSuccessStatusCode();
            (await PutAudienceAsync(analista, id, Loyalty)).EnsureSuccessStatusCode();
        });
        var satellite = Satellite(_factory);

        var member = await GrantedAsync(satellite, PurchaseFor(open, new { document = TestCpf.New(), loyaltyMember = true }));
        var other = await GrantedAsync(satellite, PurchaseFor(open, new { document = _document, loyaltyMember = false }));

        Assert.Equal(restricted.CampaignId, member.Voucher!.CampaignId);
        Assert.Equal(open.CampaignId, other.Voucher!.CampaignId);
    }

    [Fact]
    public async Task Grant_RepeatedTransaction_ReturnsTheFirstDecisionEvenWithOtherAttributes()
    {
        var scenario = await RestrictedCampaignAsync(Loyalty);
        var satellite = Satellite(_factory);

        var first = await NotGrantedAsync(satellite, PurchaseFor(scenario, new { document = _document, loyaltyMember = false }, "TRX-REPEAT-047"), "CustomerNotEligible");
        var repeated = await NotGrantedAsync(satellite, PurchaseFor(scenario, new { document = _document, loyaltyMember = true }, "TRX-REPEAT-047"), "CustomerNotEligible");

        Assert.Equal(first.ConcessionId, repeated.ConcessionId);
    }

    [Theory]
    [InlineData("""{"birthDate":"2030-01-01"}""", "Customer.BirthDate", EligibilityContract.BirthDateInFuture)]
    [InlineData("""{"state":"XX"}""", "Customer.State", EligibilityContract.CustomerStateInvalid)]
    [InlineData("""{"cityCode":"123"}""", "Customer.CityCode", EligibilityContract.CustomerCityCodeInvalid)]
    [InlineData("""{"history":[{"windowDays":90},{"windowDays":90}]}""", "Customer.History[1].WindowDays", EligibilityContract.HistoryDuplicated)]
    [InlineData("""{"history":[{"windowDays":0}]}""", "Customer.History[0].WindowDays", EligibilityContract.HistoryWindowRange)]
    public async Task Grant_WithInvalidCustomerAttributes_ReturnsValidationProblem(string attributes, string field, string message)
    {
        var scenario = await RestrictedCampaignAsync(Loyalty);
        var customer = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(attributes)!;
        customer["document"] = JsonSerializer.SerializeToElement(_document);

        var response = await Satellite(_factory).PostAsJsonAsync(ConcessionsRoute, PurchaseFor(scenario, customer));

        await AssertValidationAsync(response, field, message);
    }

    // ---------- Resgate, simulação e satélite ----------

    [Fact]
    public async Task Redeem_DoesNotReevaluateTheAudience()
    {
        var scenario = await RestrictedCampaignAsync(Loyalty);
        var satellite = Satellite(_factory);
        var granted = await GrantedAsync(satellite, PurchaseFor(scenario, new { document = _document, loyaltyMember = true }));
        var voucher = (await ConfirmedAsync(satellite, granted.ConcessionId)).Voucher!;

        // O cliente saiu do clube depois da emissão: o resgate não olha o público (US-06.4 cenário 3, RN13).
        var redemption = new
        {
            voucherId = voucher.Id,
            transactionId = $"TRX-{Guid.NewGuid():N}",
            customer = new { document = _document, loyaltyMember = false },
            establishmentId = scenario.StoreId,
            channel = "Physical",
            totalAmount = 200m,
            items = new[] { new { productCode = "MOD-001", quantity = 1, unitPrice = 200m } }
        };

        await RedeemedAsync(satellite, redemption);
    }

    [Fact]
    public async Task Simulate_ShowsEachCriterion_WithoutIssuingOrRecording()
    {
        var scenario = await RestrictedCampaignAsync(Loyalty, new { type = "AgeRange", maxAge = 30 });
        var analista = User(_factory);
        var auditBefore = await QueryAsync(_factory, db => db.AuditEntries.CountAsync());

        var eligible = await SimulateAsync(analista, scenario.CampaignId, new { document = _document, loyaltyMember = true, birthDate = "2000-01-01" });
        var notEligible = await SimulateAsync(analista, scenario.CampaignId, new { document = _document, birthDate = "1980-01-01" });

        Assert.True(eligible.Eligible);
        Assert.Equal(["Met", "Met"], eligible.Criteria.Select(criterion => criterion.Outcome));
        Assert.False(notEligible.Eligible);
        Assert.Equal(["Missing", "NotMet"], notEligible.Criteria.Select(criterion => criterion.Outcome));
        Assert.Equal(auditBefore, await QueryAsync(_factory, db => db.AuditEntries.CountAsync()));
        Assert.Equal(0, await QueryAsync(_factory, db => db.Concessions.CountAsync(concession => concession.CustomerDocument == _document)));
    }

    [Fact]
    public async Task Simulate_AllCustomers_IsEligibleWithoutCriteria_AndUnknownCampaignIs404()
    {
        var scenario = await ApprovedCampaignAsync(_factory);
        var analista = User(_factory);

        var result = await SimulateAsync(analista, scenario.CampaignId, new { document = _document });
        var missing = await analista.PostAsJsonAsync($"{ReadyCampaign.Route}/{Guid.CreateVersion7()}/eligibility/simulate", new { customer = new { document = _document } });

        Assert.True(result.Eligible);
        Assert.Empty(result.Criteria);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task SatelliteCampaigns_TellWhatToSend_WithoutRevealingTheRule()
    {
        var document = TestCpf.New();
        var scenario = await RestrictedCampaignAsync(
            Loyalty,
            new { type = "AgeRange", minAge = 18 },
            new { type = "PurchaseFrequency", minPurchases = 3, windowDays = 90 },
            new { type = "SpecificCustomers", documents = new[] { document } });

        var campaigns = await Satellite(_factory).GetFromJsonAsync<JsonElement>($"{SatelliteCampaignsRoute}?establishmentId={scenario.StoreId}");
        var campaign = campaigns.EnumerateArray().Single(item => item.GetProperty("id").GetGuid() == scenario.CampaignId);

        Assert.Equal("Restricted", campaign.GetProperty("audience").GetString());
        Assert.Equal(["birthDate", "loyaltyMember", "history.purchaseCount"], campaign.GetProperty("requiredAttributes").EnumerateArray().Select(item => item.GetString()));
        Assert.Equal([90], campaign.GetProperty("historyWindows").EnumerateArray().Select(item => item.GetInt32()));
        Assert.DoesNotContain(document, campaign.GetRawText());
        Assert.DoesNotContain("minAge", campaign.GetRawText());
    }

    // ---------- Ajustes do agente de testes (PR #60) ----------

    [Fact]
    public async Task PutAudience_ConcurrentEdits_NeverMergeTheLists()
    {
        _factory.Clock.UtcNow = Setup;
        var analista = User(_factory);
        var id = await ReadyCampaign.CreateAsync(analista);
        var lists = Enumerable.Range(0, 4).Select(_ => Enumerable.Range(0, 50).Select(_ => TestCpf.New()).ToArray()).ToList();

        var responses = await Task.WhenAll(lists.Select(list =>
            PutAudienceAsync(User(_factory), id, Loyalty, new { type = "SpecificCustomers", documents = list })));

        Assert.All(responses, response => Assert.True(
            response.StatusCode is HttpStatusCode.OK or HttpStatusCode.UnprocessableEntity, response.StatusCode.ToString()));
        Assert.Contains(responses, response => response.StatusCode == HttpStatusCode.OK);
        foreach (var refused in responses.Where(response => response.StatusCode != HttpStatusCode.OK))
        {
            Assert.Equal("CAMPAIGN_CONCURRENT_UPDATE", (await refused.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        }

        var criteria = await QueryAsync(_factory, db => db.CampaignEligibilityCriteria.AsNoTracking().Where(item => item.CampaignId == id).ToListAsync());
        var stored = await QueryAsync(_factory, db => db.CampaignEligibleCustomers.AsNoTracking()
            .Where(item => item.CampaignId == id).Select(item => item.Document).ToListAsync());
        Assert.Equal(2, criteria.Count);
        Assert.Contains(lists, list => list.Order().SequenceEqual(stored.Order()));
    }

    [Fact]
    public async Task Grant_RepeatedTransactionWithInvalidAttributes_StillReturnsTheFirstDecision()
    {
        var scenario = await RestrictedCampaignAsync(Loyalty);
        var satellite = Satellite(_factory);
        var first = await GrantedAsync(satellite, PurchaseFor(scenario, new { document = _document, loyaltyMember = true }, "TRX-REPEAT-INVALID-047"));

        var repeated = await GrantAsync(satellite, PurchaseFor(scenario, new { document = _document, birthDate = "2099-01-01" }, "TRX-REPEAT-INVALID-047"));

        repeated.Response.EnsureSuccessStatusCode();
        Assert.Equal(first.ConcessionId, repeated.Body!.ConcessionId);
    }

    [Fact]
    public async Task Grant_AgeUsesTheNetworkDate_EvenWhenUtcIsAlreadyTheNextDay()
    {
        var scenario = await RestrictedCampaignAsync(new { type = "AgeRange", minAge = 18 });
        var satellite = Satellite(_factory);

        // 23:30 de 10/10 em Brasília já é 11/10 em UTC: quem nasceu em 11/10/2008 ainda tem 17 na data da rede.
        _factory.Clock.UtcNow = new DateTimeOffset(2026, 10, 10, 23, 30, 0, TimeSpan.FromHours(-3));

        await NotGrantedAsync(satellite, PurchaseFor(scenario, new { document = _document, birthDate = "2008-10-11" }), "CustomerNotEligible");
        await GrantedAsync(satellite, PurchaseFor(scenario, new { document = _document, birthDate = "2008-10-10" }));
    }

    [Fact]
    public async Task PutAudience_EditedInDraft_TheConcessionUsesOnlyTheCurrentCombination()
    {
        var scenario = await ApprovedCampaignAsync(_factory, async (analista, id) =>
        {
            (await PutAudienceAsync(analista, id, Loyalty)).EnsureSuccessStatusCode();
            (await PutAudienceAsync(analista, id, new { type = "AgeRange", minAge = 60 })).EnsureSuccessStatusCode();
        });
        var satellite = Satellite(_factory);

        await NotGrantedAsync(satellite, PurchaseFor(scenario, new { document = _document, loyaltyMember = true }), "CustomerNotEligible");
        await GrantedAsync(satellite, PurchaseFor(scenario, new { document = _document, birthDate = "1950-01-01" }));
    }

    [Fact]
    public async Task PutAudience_MoreThanTenThousandDocuments_IsRejected()
    {
        _factory.Clock.UtcNow = Setup;
        var analista = User(_factory);
        var id = await ReadyCampaign.CreateAsync(analista);
        var documents = Enumerable.Range(0, EligibilityContract.MaxDocuments + 1).Select(_ => "11144477735").ToArray();

        var response = await PutAudienceAsync(analista, id, new { type = "SpecificCustomers", documents });

        await AssertValidationAsync(response, "Criteria[0].Documents", EligibilityContract.DocumentsTooMany);
    }

    [Theory]
    [InlineData("page=0", "page", EligibilityContract.PageInvalid)]
    [InlineData("pageSize=0", "pageSize", EligibilityContract.PageSizeInvalid)]
    [InlineData("pageSize=501", "pageSize", EligibilityContract.PageSizeInvalid)]
    public async Task GetEligibleCustomers_WithInvalidPage_ReturnsValidationProblem(string query, string field, string message)
    {
        _factory.Clock.UtcNow = Setup;
        var analista = User(_factory);
        var id = await ReadyCampaign.CreateAsync(analista);

        var response = await analista.GetAsync($"{ReadyCampaign.Route}/{id}/audience/customers?{query}");

        await AssertValidationAsync(response, field, message);
    }

    [Fact]
    public async Task NewRoutes_RequireAnAnalystOrManager()
    {
        _factory.Clock.UtcNow = Setup;
        var id = await ReadyCampaign.CreateAsync(User(_factory));
        var customers = $"{ReadyCampaign.Route}/{id}/audience/customers";
        var simulate = $"{ReadyCampaign.Route}/{id}/eligibility/simulate";
        var body = new { customer = new { document = _document } };
        var admin = User(_factory, Roles.Administrador);
        var anonymous = _factory.CreateClient();

        Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync(customers)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.PostAsJsonAsync(simulate, body)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(customers)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync(simulate, body)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await User(_factory, Roles.Gestor).PostAsJsonAsync(simulate, body)).StatusCode);
    }

    // ---------- Apoio ----------

    private async Task<Scenario> RestrictedCampaignAsync(params object[] criteria) =>
        await ApprovedCampaignAsync(_factory, async (analista, id) => (await PutAudienceAsync(analista, id, criteria)).EnsureSuccessStatusCode());

    private static Task<HttpResponseMessage> PutAudienceAsync(HttpClient analista, Guid id, params object[] criteria) =>
        analista.PutAsJsonAsync($"{ReadyCampaign.Route}/{id}/audience", new { audienceType = "Restricted", criteria });

    private static object PurchaseFor(Scenario scenario, object customer, string? transactionId = null) => new
    {
        transactionId = transactionId ?? $"TRX-{Guid.NewGuid():N}",
        customer,
        establishmentId = scenario.StoreId,
        channel = "Physical",
        totalAmount = 200m,
        items = new[] { new { productCode = "MOD-001", quantity = 1, unitPrice = 200m } }
    };

    private async Task<JsonElement> SnapshotAsync(Guid concessionId)
    {
        var json = await QueryAsync(_factory, db => db.Concessions.Where(item => item.Id == concessionId).Select(item => item.Eligibility).SingleAsync());
        return JsonDocument.Parse(json!).RootElement;
    }

    private static async Task<EligibilitySimulationResponse> SimulateAsync(HttpClient analista, Guid campaignId, object customer)
    {
        var response = await analista.PostAsJsonAsync($"{ReadyCampaign.Route}/{campaignId}/eligibility/simulate", new { customer });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());

        return (await response.Content.ReadFromJsonAsync<EligibilitySimulationResponse>())!;
    }

    private static string Masked(string cpf) => $"{cpf[..3]}.{cpf[3..6]}.{cpf[6..9]}-{cpf[9..]}";

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

/// <summary>CPFs válidos (dígito verificador) aleatórios para os testes do público restrito.</summary>
internal static class TestCpf
{
    public static string New()
    {
        var digits = Enumerable.Range(0, 9).Select(_ => Random.Shared.Next(10)).ToList();
        digits.Add(Check(digits));
        digits.Add(Check(digits));
        var cpf = string.Concat(digits);

        return Domain.Common.Cpf.IsValid(cpf) ? cpf : New();
    }

    private static int Check(List<int> digits)
    {
        var sum = digits.Select((digit, index) => digit * (digits.Count + 1 - index)).Sum();
        var rest = sum % 11;
        return rest < 2 ? 0 : 11 - rest;
    }
}
