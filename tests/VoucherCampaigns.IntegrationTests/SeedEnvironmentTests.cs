using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VoucherCampaigns.Domain.Organization;
using VoucherCampaigns.Infrastructure.Persistence;
using VoucherCampaigns.Infrastructure.Persistence.Seed;

namespace VoucherCampaigns.IntegrationTests;

/// <summary>Task 1: o seed de demonstração só roda quando habilitado, uma única vez, e não em produção por padrão.</summary>
public class SeedEnvironmentTests
{
    private static readonly Dictionary<string, string?> ProductionSettings = new()
    {
        // Fora de Development não há chave local; sem ela a API não inicia.
        ["Auth:Jwt:SigningKey"] = "chave-de-teste-com-mais-de-trinta-e-dois-caracteres"
    };

    [Fact]
    public async Task Seed_WhenEnabled_CreatesDemoNetworkOnlyOnce()
    {
        var connectionString = ApiFactory.NewSqliteConnectionString();

        try
        {
            using (var first = ApiFactory.For("Sqlite", connectionString, seedDemoData: "true"))
            {
                first.CreateClient();
                Assert.Equal(1, await CountNetworksAsync(first));
            }

            // Segunda subida sobre o mesmo banco: o seed não pode duplicar os dados.
            using (var second = ApiFactory.For("Sqlite", connectionString, seedDemoData: "true"))
            {
                second.CreateClient();
                Assert.Equal(1, await CountNetworksAsync(second));
                Assert.True(await HasDemoNetworkAsync(second));
            }
        }
        finally
        {
            ApiFactory.DeleteSqliteDatabase(connectionString);
        }
    }

    [Fact]
    public async Task Seed_WhenDisabled_DoesNotCreateData()
    {
        var connectionString = ApiFactory.NewSqliteConnectionString();

        try
        {
            using var factory = ApiFactory.For("Sqlite", connectionString, seedDemoData: "false");
            factory.CreateClient();

            Assert.Equal(0, await CountNetworksAsync(factory));
        }
        finally
        {
            ApiFactory.DeleteSqliteDatabase(connectionString);
        }
    }

    [Fact]
    public async Task Seed_InProductionWithoutSeedSetting_StartsAndDoesNotCreateData()
    {
        var connectionString = ApiFactory.NewSqliteConnectionString();

        try
        {
            using var root = ApiFactory.For("Sqlite", connectionString, settings: ProductionSettings);
            using var production = root.WithWebHostBuilder(builder => builder.UseEnvironment("Production"));

            var health = await production.CreateClient().GetAsync("/health");

            Assert.True(health.IsSuccessStatusCode);
            Assert.Equal(0, await CountNetworksAsync(production));
        }
        finally
        {
            ApiFactory.DeleteSqliteDatabase(connectionString);
        }
    }

    // TESTE DE DIAGNÓSTICO: documenta a lacuna "o seed não deve rodar automaticamente em produção".
    [Fact]
    public async Task Seed_InProductionWithSeedEnabled_DoesNotCreateData()
    {
        var connectionString = ApiFactory.NewSqliteConnectionString();

        try
        {
            using var root = ApiFactory.For("Sqlite", connectionString, seedDemoData: "true", settings: ProductionSettings);
            using var production = root.WithWebHostBuilder(builder => builder.UseEnvironment("Production"));

            try
            {
                production.CreateClient();
            }
            catch (InvalidOperationException)
            {
                return;
            }

            Assert.Equal(0, await CountNetworksAsync(production));
        }
        finally
        {
            ApiFactory.DeleteSqliteDatabase(connectionString);
        }
    }

    private static async Task<int> CountNetworksAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        return await context.Set<Network>().CountAsync();
    }

    private static async Task<bool> HasDemoNetworkAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        return await context.Set<Network>().AnyAsync(network => network.Id == DemoDataSeeder.NetworkId);
    }
}
