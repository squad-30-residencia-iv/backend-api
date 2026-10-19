using DotNetEnv;
using VoucherCampaigns.Api.Authentication;
using VoucherCampaigns.Api.Errors;
using VoucherCampaigns.Application.Campaigns;
using VoucherCampaigns.Application.SystemStatus;
using VoucherCampaigns.Infrastructure;
using VoucherCampaigns.Infrastructure.Authentication;
using VoucherCampaigns.Infrastructure.Persistence;

// Carrega o .env (se existir) antes do host, sem sobrescrever variáveis já definidas no ambiente.
Env.NoClobber().TraversePath().Load();

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers()
    .ConfigureApiBehaviorOptions(options => options.InvalidModelStateResponseFactory = ApiProblems.Validation);
builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();

builder.Services.AddScoped<ISystemStatusService, SystemStatusService>();
builder.Services.AddScoped<ICampaignService, CampaignService>();
builder.Services.AddInfrastructure();
builder.Services.AddApiAuthentication();

builder.Services.AddHealthChecks()
    .AddDbContextCheck<AppDbContext>();

var app = builder.Build();

// Falha na inicialização se a seção Auth estiver inválida, em vez de na primeira requisição.
app.Services.GetRequiredService<AuthOptions>();

await app.Services.MigrateDatabaseAsync();

app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks("/health");

app.Run();
