using Microsoft.AspNetCore.Authentication.JwtBearer;
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
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();

        // Configurado sob demanda para usar o AuthOptions da configuração final do host.
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<AuthOptions>(ConfigureJwtBearer);

        services.AddAuthorizationBuilder()
            .AddPolicy(AuthorizationPolicies.ManageCampaigns, policy => policy.RequireRole(Roles.Analista, Roles.Gestor))
            .AddPolicy(AuthorizationPolicies.ApproveCampaigns, policy => policy.RequireRole(Roles.Gestor));

        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, HttpCurrentUser>();

        return services;
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

        options.Events = new JwtBearerEvents
        {
            OnChallenge = async context =>
            {
                context.HandleResponse();
                context.Response.Headers.WWWAuthenticate = JwtBearerDefaults.AuthenticationScheme;

                await ApiProblems.WriteAsync(
                    context.HttpContext, StatusCodes.Status401Unauthorized, ErrorCodes.Unauthenticated, ApiProblems.UnauthenticatedDetail);
            },
            OnForbidden = context => ApiProblems.WriteAsync(
                context.HttpContext, StatusCodes.Status403Forbidden, ErrorCodes.ForbiddenRole, ApiProblems.ForbiddenRoleDetail)
        };
    }
}
