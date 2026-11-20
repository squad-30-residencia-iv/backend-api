using DotNetEnv;
using Microsoft.Net.Http.Headers;
using VoucherCampaigns.Api.Authentication;
using VoucherCampaigns.Api.Cors;
using VoucherCampaigns.Api.Errors;
using VoucherCampaigns.Api.Vouchers;
using VoucherCampaigns.Application.Audit;
using VoucherCampaigns.Application.Campaigns;
using VoucherCampaigns.Application.Catalog;
using VoucherCampaigns.Application.Dashboard;
using VoucherCampaigns.Application.Organization;
using VoucherCampaigns.Application.Reports;
using VoucherCampaigns.Application.SystemStatus;
using VoucherCampaigns.Application.Vouchers;
using VoucherCampaigns.Infrastructure;
using VoucherCampaigns.Infrastructure.Authentication;
using VoucherCampaigns.Infrastructure.Persistence;

// O .env é conveniência local (spec GH-066, D-7; SEC-21): só em Development, sem sobrescrever variáveis já definidas.
// Fora de Development a configuração vem só do ambiente e dos appsettings.
var environmentName = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
    ?? Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT");

if (string.Equals(environmentName, Environments.Development, StringComparison.OrdinalIgnoreCase))
{
    Env.NoClobber().TraversePath().Load();
}

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers()
    .ConfigureApiBehaviorOptions(options => options.InvalidModelStateResponseFactory = ApiProblems.Validation);
builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();

builder.Services.AddScoped<ISystemStatusService, SystemStatusService>();
builder.Services.AddScoped<ICampaignService, CampaignService>();
builder.Services.AddScoped<ICategoryService, CategoryService>();
builder.Services.AddScoped<IProductService, ProductService>();
builder.Services.AddScoped<INetworkService, NetworkService>();
builder.Services.AddScoped<IRegionService, RegionService>();
builder.Services.AddScoped<IEstablishmentService, EstablishmentService>();
builder.Services.AddScoped<IConcessionService, ConcessionService>();
builder.Services.AddScoped<IRedemptionService, RedemptionService>();
builder.Services.AddScoped<ISatelliteCampaignService, SatelliteCampaignService>();
builder.Services.AddScoped<IPortalVoucherQueryService, PortalVoucherQueryService>();
builder.Services.AddScoped<IReportService, ReportService>();
builder.Services.AddScoped<IAuditQueryService, AuditQueryService>();
builder.Services.AddScoped<IDashboardService, DashboardService>();
builder.Services.AddHostedService<ReservationSweepService>();
builder.Services.AddInfrastructure();
builder.Services.AddApiAuthentication();
builder.Services.AddLoginRateLimit();
builder.Services.AddCors();
builder.Services.AddSingleton(provider =>
    PortalCorsOptions.FromConfiguration(provider.GetRequiredService<IConfiguration>()));

builder.Services.AddHealthChecks()
    .AddDbContextCheck<AppDbContext>();

var app = builder.Build();

// Falha na inicialização se Auth ou Cors estiverem inválidos, em vez de na primeira requisição.
var authOptions = app.Services.GetRequiredService<AuthOptions>();

// A chave de desenvolvimento está no repositório (spec GH-066, D-7; SEC-07): fora de Development, a API não sobe com ela.
if (!app.Environment.IsDevelopment()
    && authOptions.SigningKey.Contains(AuthOptions.DevelopmentKeyMarker, StringComparison.OrdinalIgnoreCase))
{
    throw new InvalidOperationException(
        "A chave JWT de desenvolvimento não pode ser usada fora de Development. Defina Auth__Jwt__SigningKey.");
}

app.Services.GetRequiredService<LoginRateLimitOptions>();
app.Services.GetRequiredService<SatelliteAuthOptions>();
app.Services.GetRequiredService<VoucherOptions>();
var portalCors = app.Services.GetRequiredService<PortalCorsOptions>();

await app.Services.MigrateDatabaseAsync();

app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

if (portalCors.AllowedOrigins.Count > 0)
{
    app.UseCors(policy => policy
        .WithOrigins([.. portalCors.AllowedOrigins])
        .WithHeaders(HeaderNames.Authorization, HeaderNames.ContentType)
        .WithMethods(HttpMethods.Get, HttpMethods.Post, HttpMethods.Put, HttpMethods.Delete));
}

app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapControllers();
app.MapHealthChecks("/health");

app.Run();
