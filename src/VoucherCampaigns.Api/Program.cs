using DotNetEnv;
using VoucherCampaigns.Application.SystemStatus;
using VoucherCampaigns.Infrastructure;
using VoucherCampaigns.Infrastructure.Persistence;

// Carrega o .env (se existir) antes do host, sem sobrescrever variáveis já definidas no ambiente.
Env.NoClobber().TraversePath().Load();

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();

builder.Services.AddScoped<ISystemStatusService, SystemStatusService>();
builder.Services.AddInfrastructure();

builder.Services.AddHealthChecks()
    .AddDbContextCheck<AppDbContext>();

var app = builder.Build();

await app.Services.MigrateDatabaseAsync();

app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks("/health");

app.Run();
