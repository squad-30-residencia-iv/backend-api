using VoucherCampaigns.Domain.Campaigns;
using VoucherCampaigns.Domain.Common;
using VoucherCampaigns.Domain.Eligibility;
using VoucherCampaigns.Domain.Vouchers;
using static VoucherCampaigns.UnitTests.Vouchers.MotorFixtures;

namespace VoucherCampaigns.UnitTests.Vouchers;

/// <summary>Avaliação do público restrito (spec GH-047): critério a critério, AND, atributo ausente e passo no motor.</summary>
public class EligibilityEvaluatorTests
{
    private static readonly DateOnly PurchaseDate = new(2026, 10, 10);
    private static readonly IReadOnlyDictionary<Guid, int> NoVouchers = new Dictionary<Guid, int>();
    private static int _sequence;

    [Theory]
    [InlineData("2008-10-10", 18)]
    [InlineData("2008-10-11", 17)]
    [InlineData("2000-02-29", 26)]
    public void AgeOn_CountsCompleteYears_WithTheBirthdayOnTheDay(string birthDate, int expected) =>
        Assert.Equal(expected, EligibilityEvaluator.AgeOn(DateOnly.Parse(birthDate), PurchaseDate));

    [Theory]
    [InlineData(18, 30, "2000-01-01", CriterionOutcome.Met)]
    [InlineData(18, null, "2008-10-11", CriterionOutcome.NotMet)]
    [InlineData(null, 25, "1990-01-01", CriterionOutcome.NotMet)]
    [InlineData(18, 30, null, CriterionOutcome.Missing)]
    public void AgeRange_IsInclusiveAndMissingWithoutBirthDate(int? min, int? max, string? birthDate, CriterionOutcome expected)
    {
        var criterion = Criterion(EligibilityCriterionType.AgeRange, c => { c.MinAge = min; c.MaxAge = max; });
        var customer = Customer() with { BirthDate = birthDate is null ? null : DateOnly.Parse(birthDate) };

        Assert.Equal(expected, Single(criterion, customer).Outcome);
    }

    [Theory]
    [InlineData("SP", null, CriterionOutcome.Met)]
    [InlineData("RJ", "4106902", CriterionOutcome.Met)]
    [InlineData("RJ", "3304557", CriterionOutcome.NotMet)]
    [InlineData(null, null, CriterionOutcome.Missing)]
    public void Location_StatesOrCities(string? state, string? city, CriterionOutcome expected)
    {
        var criterion = Criterion(EligibilityCriterionType.Location, c => { c.States = ["SP"]; c.CityCodes = ["4106902"]; });

        Assert.Equal(expected, Single(criterion, Customer() with { State = state, CityCode = city }).Outcome);
    }

    [Fact]
    public void History_UsesOnlyTheWindowOfTheCriterion()
    {
        var spent = Criterion(EligibilityCriterionType.TotalSpent, c => { c.MinAmount = 500; c.WindowDays = 90; });
        var frequency = Criterion(EligibilityCriterionType.PurchaseFrequency, c => { c.MinPurchases = 3; c.WindowDays = 30; });
        var category = Criterion(EligibilityCriterionType.PurchasedCategory, c => { c.CategoryId = PapelariaId; c.WindowDays = 30; });
        var customer = Customer() with
        {
            History = [new CustomerHistoryWindow(30, 900, 3, [PapelariaId]), new CustomerHistoryWindow(90, 499.99m, null, null)]
        };

        var result = EligibilityEvaluator.Evaluate([spent, frequency, category], customer, PurchaseDate, isListed: false);

        Assert.Equal([CriterionOutcome.NotMet, CriterionOutcome.Met, CriterionOutcome.Met], result.Criteria.Select(item => item.Outcome));
        Assert.False(result.Eligible);
        Assert.Equal(EligibilityCriterionType.TotalSpent, result.FirstFailed!.Type);
    }

    [Fact]
    public void AllCriteria_MustBeMet_AndTheListIsJustAnotherCriterion()
    {
        var loyalty = Criterion(EligibilityCriterionType.LoyaltyMember);
        var list = Criterion(EligibilityCriterionType.SpecificCustomers, c => c.CustomerCount = 1);
        var member = Customer() with { LoyaltyMember = true };

        Assert.True(EligibilityEvaluator.Evaluate([loyalty, list], member, PurchaseDate, isListed: true).Eligible);
        Assert.False(EligibilityEvaluator.Evaluate([loyalty, list], member, PurchaseDate, isListed: false).Eligible);
        Assert.False(EligibilityEvaluator.Evaluate([loyalty, list], Customer(), PurchaseDate, isListed: true).Eligible);
    }

    [Fact]
    public void Evaluate_NotEligibleCampaign_IsSkippedAndTheNextWins()
    {
        var best = ActiveCampaign("Restrita", items: Item(TargetType.All, BenefitType.Percentage, 50));
        var open = ActiveCampaign("Aberta");
        var eligibility = new Dictionary<Guid, EligibilityResult> { [best.Id] = new(false, []) };

        var result = ConcessionEvaluator.Evaluate([best, open], Purchase(), NoVouchers, DuringPeriod, eligibility);

        Assert.Equal(open.Id, Assert.Single(result.Eligible).Campaign.Id);
    }

    [Fact]
    public void Evaluate_OnlyCampaignNotEligible_RefusesWithCustomerNotEligible()
    {
        var campaign = ActiveCampaign();
        var eligibility = new Dictionary<Guid, EligibilityResult> { [campaign.Id] = new(false, []) };

        var result = ConcessionEvaluator.Evaluate([campaign], Purchase(), NoVouchers, DuringPeriod, eligibility);

        Assert.Empty(result.Eligible);
        Assert.Equal(ConcessionRefusal.CustomerNotEligible, result.Refusal);
        Assert.Equal(campaign.Id, result.RefusedCampaignId);
    }

    [Theory]
    [InlineData("11144477735", true)]
    [InlineData("52998224725", true)]
    [InlineData("11144477734", false)]
    [InlineData("11111111111", false)]
    [InlineData("1234567890", false)]
    public void Cpf_ChecksTheVerifierDigits(string digits, bool expected) => Assert.Equal(expected, Cpf.IsValid(digits));

    private static CustomerProfile Customer() => CustomerProfile.OnlyDocument("11144477735");

    private static CriterionResult Single(CampaignEligibilityCriterion criterion, CustomerProfile customer) =>
        Assert.Single(EligibilityEvaluator.Evaluate([criterion], customer, PurchaseDate, isListed: false).Criteria);

    private static CampaignEligibilityCriterion Criterion(EligibilityCriterionType type, Action<CampaignEligibilityCriterion>? configure = null)
    {
        var criterion = new CampaignEligibilityCriterion
        {
            Id = Guid.CreateVersion7(new DateTimeOffset(Created).AddMilliseconds(Interlocked.Increment(ref _sequence))),
            Type = type
        };
        configure?.Invoke(criterion);

        return criterion;
    }
}
