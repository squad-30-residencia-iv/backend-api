using Microsoft.EntityFrameworkCore;
using VoucherCampaigns.Domain.Catalog;
using VoucherCampaigns.Domain.Organization;

namespace VoucherCampaigns.Infrastructure.Persistence.Seed;

/// <summary>
/// Dados de demonstração para desenvolvimento local: 1 rede, 1 região, 3 lojas com canais,
/// 3 categorias e 3 produtos. Os IDs são fixos para poderem ser usados no frontend e em testes manuais.
/// Só roda quando o banco ainda não tem nenhuma rede.
/// </summary>
public static class DemoDataSeeder
{
    public static readonly Guid NetworkId = Guid.Parse("0199e3a0-0000-7000-8000-000000000001");
    public static readonly Guid RegionId = Guid.Parse("0199e3a0-0000-7000-8000-000000000101");
    public static readonly Guid StoreCenterId = Guid.Parse("0199e3a0-0000-7000-8000-000000000201");
    public static readonly Guid StoreShoppingId = Guid.Parse("0199e3a0-0000-7000-8000-000000000202");
    public static readonly Guid StoreOnlineId = Guid.Parse("0199e3a0-0000-7000-8000-000000000203");
    public static readonly Guid FashionCategoryId = Guid.Parse("0199e3a0-0000-7000-8000-000000000301");
    public static readonly Guid ShoesCategoryId = Guid.Parse("0199e3a0-0000-7000-8000-000000000302");
    public static readonly Guid HomeCategoryId = Guid.Parse("0199e3a0-0000-7000-8000-000000000303");

    public static void Seed(DbContext context)
    {
        if (context.Set<Network>().Any())
        {
            return;
        }

        AddDemoData(context);
        context.SaveChanges();
    }

    public static async Task SeedAsync(DbContext context, CancellationToken cancellationToken)
    {
        if (await context.Set<Network>().AnyAsync(cancellationToken))
        {
            return;
        }

        AddDemoData(context);
        await context.SaveChangesAsync(cancellationToken);
    }

    private static void AddDemoData(DbContext context)
    {
        var network = new Network
        {
            Id = NetworkId,
            Name = "Supermercados Norte",
            TimeZone = "America/Sao_Paulo",
            Regions =
            [
                new Region
                {
                    Id = RegionId,
                    Name = "Região Norte",
                    Establishments =
                    [
                        Store(StoreCenterId, "001", "Loja Centro", ChannelType.Physical),
                        Store(StoreShoppingId, "002", "Loja Shopping", ChannelType.Physical),
                        Store(StoreOnlineId, "003", "Loja Online", ChannelType.Virtual)
                    ]
                }
            ]
        };

        Category[] categories =
        [
            new()
            {
                Id = FashionCategoryId,
                Name = "Moda",
                Products = [new Product { Code = "MOD-001", Name = "Camiseta básica" }]
            },
            new()
            {
                Id = ShoesCategoryId,
                Name = "Calçados",
                Products = [new Product { Code = "CAL-001", Name = "Tênis casual" }]
            },
            new()
            {
                Id = HomeCategoryId,
                Name = "Casa",
                Products = [new Product { Code = "CAS-001", Name = "Jogo de toalhas" }]
            }
        ];

        context.Add(network);
        context.AddRange(categories);
    }

    private static Establishment Store(Guid id, string code, string name, ChannelType channel) => new()
    {
        Id = id,
        Code = code,
        Name = name,
        Channels = [new Channel { Type = channel }]
    };
}
