using DotNetEnv;
using VoucherCampaigns.Application.SystemStatus;
using VoucherCampaigns.Infrastructure;
using VoucherCampaigns.Infrastructure.Persistence;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using VoucherCampaigns.Infrastructure.Authentication;

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
// 1. Injeta o serviço
builder.Services.AddScoped<JwtAuthService>();

// 2. Configura o esquema JWT
var key = Encoding.ASCII.GetBytes("chave-super-secreta-para-assinatura-jwt-2026-vouchers");
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(key),
            ValidateIssuer = false,
            ValidateAudience = false
        };
    });

builder.Services.AddAuthorization();
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
app.UseAuthentication();
app.MapControllers();
app.MapHealthChecks("/health");

app.Run();
