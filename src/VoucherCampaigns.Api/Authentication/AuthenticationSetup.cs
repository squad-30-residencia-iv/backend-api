using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using VoucherCampaigns.Api.Errors;
using VoucherCampaigns.Application.Authentication;
using VoucherCampaigns.Infrastructure.Authentication;

namespace VoucherCampaigns.Api.Authentication;

public static class AuthenticationSetup
{
    public static IServiceCollection AddApiAuthentication(this IServiceCollection services)
    {
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer()
            .AddJwtBearer(SatelliteAuthentication.Scheme);

        // Configurado sob demanda para usar o AuthOptions da configuração final do host.
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<AuthOptions>(ConfigureJwtBearer);
        services.AddOptions<JwtBearerOptions>(SatelliteAuthentication.Scheme)
            .Configure<AuthOptions, SatelliteAuthOptions>(ConfigureSatelliteBearer);

        services.AddAuthorizationBuilder()
            .AddPolicy(AuthorizationPolicies.ManageCampaigns, policy => policy.RequireRole(Roles.Analista, Roles.Gestor))
            .AddPolicy(AuthorizationPolicies.SubmitCampaigns, policy => policy.RequireRole(Roles.Analista))
            .AddPolicy(AuthorizationPolicies.ApproveCampaigns, policy => policy.RequireRole(Roles.Gestor))
            .AddPolicy(AuthorizationPolicies.ReadHierarchy, policy => policy.RequireRole(Roles.Administrador, Roles.Analista, Roles.Gestor))
            .AddPolicy(AuthorizationPolicies.ManageHierarchy, policy => policy.RequireRole(Roles.Administrador))
            .AddPolicy(AuthorizationPolicies.ReadCatalog, policy => policy.RequireRole(Roles.Administrador, Roles.Analista, Roles.Gestor))
            .AddPolicy(AuthorizationPolicies.ManageCatalog, policy => policy.RequireRole(Roles.Administrador))
            .AddPolicy(AuthorizationPolicies.ManageSatelliteApps, policy => policy.RequireRole(Roles.Administrador))
            .AddPolicy(AuthorizationPolicies.SatelliteApp, policy => SatellitePolicy(policy))
            .AddPolicy(AuthorizationPolicies.SatelliteReadCampaigns, policy => SatellitePolicy(policy, SatelliteScopes.ReadCampaigns))
            .AddPolicy(AuthorizationPolicies.SatelliteIssueVouchers, policy => SatellitePolicy(policy, SatelliteScopes.IssueVouchers))
            .AddPolicy(AuthorizationPolicies.SatelliteRedeemVouchers, policy => SatellitePolicy(policy, SatelliteScopes.RedeemVouchers));

        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, HttpCurrentUser>();
        services.AddScoped<ICurrentSatelliteApp, HttpCurrentSatelliteApp>();

        return services;
    }

    /// <summary>Só o esquema da aplicação satélite autentica a rota; escopo ausente responde 403 AUTH_FORBIDDEN_SCOPE.</summary>
    private static void SatellitePolicy(AuthorizationPolicyBuilder policy, string? scope = null)
    {
        policy.AddAuthenticationSchemes(SatelliteAuthentication.Scheme)
            .RequireAuthenticatedUser()
            .RequireClaim(AuthClaimTypes.ClientId);

        if (scope is not null)
        {
            policy.RequireClaim(AuthClaimTypes.Scope, scope);
        }
    }

    private static void ConfigureJwtBearer(JwtBearerOptions options, AuthOptions authOptions)
    {
        // Mantém os nomes das claims do token (sub, role, escopoId) sem o mapeamento para ClaimTypes.
        options.MapInboundClaims = false;

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = JwtAuthService.CreateSigningKey(authOptions),
            ValidIssuer = authOptions.Issuer,
            ValidAudience = authOptions.Audience,
            ClockSkew = TimeSpan.FromMinutes(1),
            NameClaimType = AuthClaimTypes.UserId,
            RoleClaimType = AuthClaimTypes.Role
        };

        options.Events = ProblemEvents(ErrorCodes.ForbiddenRole, ApiProblems.ForbiddenRoleDetail);
    }

    /// <summary>Mesmo emissor e chave do token de usuário, com a audiência das aplicações satélites (US-07.5).</summary>
    private static void ConfigureSatelliteBearer(
        JwtBearerOptions options, AuthOptions authOptions, SatelliteAuthOptions satelliteOptions)
    {
        options.MapInboundClaims = false;

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = JwtAuthService.CreateSigningKey(authOptions),
            ValidIssuer = authOptions.Issuer,
            ValidAudience = satelliteOptions.Audience,
            ClockSkew = TimeSpan.FromMinutes(1),
            NameClaimType = AuthClaimTypes.ClientId,
            AuthenticationType = SatelliteAuthentication.Scheme
        };

        options.Events = ProblemEvents(ErrorCodes.ForbiddenScope, ApiProblems.ForbiddenScopeDetail);
    }

    private static JwtBearerEvents ProblemEvents(string forbiddenCode, string forbiddenDetail) => new()
    {
        OnChallenge = async context =>
        {
            context.HandleResponse();
            context.Response.Headers.WWWAuthenticate = JwtBearerDefaults.AuthenticationScheme;

            await ApiProblems.WriteAsync(
                context.HttpContext, StatusCodes.Status401Unauthorized, ErrorCodes.Unauthenticated, ApiProblems.UnauthenticatedDetail);
        },
        OnForbidden = context => ApiProblems.WriteAsync(
            context.HttpContext, StatusCodes.Status403Forbidden, forbiddenCode, forbiddenDetail)
    };
}
