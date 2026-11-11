using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VoucherCampaigns.Application.Authentication;
using VoucherCampaigns.Application.Campaigns;
using VoucherCampaigns.Application.DTOs.Organization;
using VoucherCampaigns.Domain.Campaigns;
using VoucherCampaigns.Infrastructure.Persistence;
using VoucherCampaigns.Infrastructure.Persistence.Seed;
using VoucherCampaigns.IntegrationTests.Authentication;
using VoucherCampaigns.IntegrationTests.Campaigns;

namespace VoucherCampaigns.IntegrationTests.Vouchers;

/// <summary>Campanha aprovada e a loja criada só para ela: as campanhas dos outros testes não alcançam essa loja.</summary>
internal sealed record Scenario(Guid CampaignId, Guid StoreId);

/// <summary>
/// Cenário do motor: a campanha é configurada e aprovada com o relógio em 28/09/2026 (antes da vigência da
/// <see cref="ReadyCampaign"/>, 01/10 08:00 a 31/10 23:59), e a concessão acontece em 10/10/2026 12:00 (Brasília),
/// quando a agendada já está efetivamente ativa. Aplicação <c>pdv-demo</c> da rede de demonstração e compra padrão
/// de R$ 200 com uma camiseta (MOD-001, categoria Moda) no canal físico.
/// </summary>
internal static class MotorScenario
{
    public const string ConcessionsRoute = "/api/v1/satellite/concessions";
    public const string Document = "123.456.789-01";
    public const string DocumentDigits = "12345678901";

    /// <summary>Relógio da configuração e da aprovação: o início da campanha não pode estar no passado (RN02).</summary>
    public static readonly DateTimeOffset Setup = new(2026, 9, 28, 12, 0, 0, TimeSpan.FromHours(-3));

    /// <summary>Relógio da concessão, dentro da vigência.</summary>
    public static readonly DateTimeOffset Now = new(2026, 10, 10, 12, 0, 0, TimeSpan.FromHours(-3));

    public static HttpClient Satellite(ApiFactory factory, string[]? scopes = null, Guid? networkId = null) =>
        factory.CreateClient().WithBearer(TestAuth.CreateSatelliteToken(factory, scopes, networkId));

    public static HttpClient User(ApiFactory factory, string role = Roles.Analista) =>
        factory.CreateClient().WithBearer(TestAuth.CreateToken(factory, role));

    /// <summary>Loja nova na região de demonstração, criada pelo administrador.</summary>
    public static async Task<Guid> NewStoreAsync(ApiFactory factory, params string[] channels)
    {
        var response = await User(factory, Roles.Administrador).PostAsJsonAsync("/api/v1/establishments", new
        {
            regionId = DemoDataSeeder.RegionId,
            code = Guid.NewGuid().ToString("N")[..8],
            name = "Loja do teste",
            channels = channels.Length == 0 ? ["Physical"] : channels
        });
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<EstablishmentResponse>())!.Id;
    }

    /// <summary>
    /// Rascunho pronto (alvo "todos" 10%, total 100, 30 dias, 1 uso), com o escopo numa loja nova (canal físico),
    /// ajustado por <paramref name="configure"/>, submetido e aprovado.
    /// </summary>
    public static async Task<Scenario> ApprovedCampaignAsync(ApiFactory factory, Func<HttpClient, Guid, Task>? configure = null)
    {
        factory.Clock.UtcNow = Setup;
        var analista = User(factory);
        var storeId = await NewStoreAsync(factory);
        var id = await ReadyCampaign.CreateAsync(analista);
        (await analista.PutAsJsonAsync($"{ReadyCampaign.Route}/{id}/scopes", new { scopes = new[] { new { establishmentId = storeId, channels = new[] { "Physical" } } } }))
            .EnsureSuccessStatusCode();

        if (configure is not null)
        {
            await configure(analista, id);
        }

        (await analista.PostAsync($"{ReadyCampaign.Route}/{id}/submit", null)).EnsureSuccessStatusCode();
        (await User(factory, Roles.Gestor).PostAsync($"{ReadyCampaign.Route}/{id}/approve", null)).EnsureSuccessStatusCode();
        factory.Clock.UtcNow = Now;

        return new Scenario(id, storeId);
    }

    public static object Purchase(
        Guid establishmentId,
        string? transactionId = null,
        string document = Document,
        string channel = "Physical",
        decimal totalAmount = 200m,
        object[]? items = null,
        int? customerPurchaseCount = null) => new
    {
        transactionId = transactionId ?? $"TRX-{Guid.NewGuid():N}",
        customer = new { document },
        establishmentId,
        channel,
        totalAmount,
        items = items ?? [new { productCode = "MOD-001", quantity = 1, unitPrice = 200m }],
        customerPurchaseCount
    };

    public static async Task<(HttpResponseMessage Response, ConcessionResponseDto? Body)> GrantAsync(HttpClient satellite, object purchase)
    {
        var response = await satellite.PostAsJsonAsync(ConcessionsRoute, purchase);
        var body = response.IsSuccessStatusCode ? await response.Content.ReadFromJsonAsync<ConcessionResponseDto>() : null;

        return (response, body);
    }

    public static async Task<ConcessionResponseDto> GrantedAsync(HttpClient satellite, object purchase)
    {
        var (response, body) = await GrantAsync(satellite, purchase);
        response.EnsureSuccessStatusCode();
        Assert.Equal("Provisional", body!.Status);

        return body;
    }

    public static async Task<ConcessionResponseDto> NotGrantedAsync(HttpClient satellite, object purchase, string reason)
    {
        var (response, body) = await GrantAsync(satellite, purchase);
        response.EnsureSuccessStatusCode();
        Assert.Equal("NotGranted", body!.Status);
        Assert.Equal(reason, body.Reason);
        Assert.Null(body.Voucher);

        return body;
    }

    public static async Task<Campaign> StoredCampaignAsync(ApiFactory factory, Guid campaignId)
    {
        await using var scope = factory.Services.CreateAsyncScope();

        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Campaigns
            .Include(campaign => campaign.Items)
            .AsNoTracking()
            .SingleAsync(campaign => campaign.Id == campaignId);
    }

    public static async Task<string> CampaignStatusAsync(ApiFactory factory, Guid campaignId) =>
        (await User(factory).GetFromJsonAsync<CampaignResponse>($"{ReadyCampaign.Route}/{campaignId}"))!.Status;

    public static async Task<T> QueryAsync<T>(ApiFactory factory, Func<AppDbContext, Task<T>> query)
    {
        await using var scope = factory.Services.CreateAsyncScope();

        return await query(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }
}

/// <summary>Cópia desserializável de <see cref="Application.Vouchers.ConcessionResponse"/>.</summary>
public record ConcessionResponseDto(
    Guid ConcessionId,
    string TransactionId,
    string Status,
    string? Reason,
    DateTimeOffset? ReservedUntil,
    VoucherResponseDto? Voucher);

public record VoucherResponseDto(
    Guid Id,
    string Code,
    Guid CampaignId,
    string CampaignName,
    string Status,
    DateTimeOffset IssuedAt,
    DateTimeOffset ValidUntil,
    int MaxUses,
    int RemainingUses,
    bool AllowUseInSamePurchase,
    VoucherTargetDto Target,
    Guid ConcessionId,
    string TransactionId);

public record VoucherTargetDto(string TargetType, Guid? CategoryId, Guid? ProductId, string BenefitType, decimal BenefitValue);
