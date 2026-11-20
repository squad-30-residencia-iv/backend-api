using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using VoucherCampaigns.Api.Errors;

namespace VoucherCampaigns.Api.Authentication;

/// <summary>
/// Limite de tentativas no <c>POST /auth/login</c> por IP (spec GH-066, D-6; SEC-01 da auditoria): janela fixa de
/// <c>Auth:LoginRateLimit:WindowSeconds</c> segundos com até <c>PermitLimit</c> tentativas; acima disso, 429
/// <c>AUTH_LOGIN_RATE_LIMITED</c> com <c>Retry-After</c>.
/// </summary>
public sealed record LoginRateLimitOptions(int PermitLimit, int WindowSeconds)
{
    public const string PolicyName = "login";
    public const string Section = "Auth:LoginRateLimit";
    public const int DefaultPermitLimit = 10;
    public const int DefaultWindowSeconds = 60;

    public static LoginRateLimitOptions FromConfiguration(IConfiguration configuration)
    {
        var (permitLimit, windowSeconds) = FixedWindowConfiguration.Read(configuration, Section, DefaultPermitLimit, DefaultWindowSeconds);

        return new LoginRateLimitOptions(permitLimit, windowSeconds);
    }
}

/// <summary>
/// Limite de pedidos no <c>POST /satellite/token</c> por IP (spec GH-071, D-2; SEC-05 da auditoria), além do bloqueio
/// por <c>clientId</c> + IP depois de segredos errados: <c>Auth:Satellite:TokenRateLimit</c>, com 429
/// <c>SATELLITE_TOKEN_RATE_LIMITED</c> e <c>Retry-After</c>.
/// </summary>
public sealed record SatelliteTokenRateLimitOptions(int PermitLimit, int WindowSeconds)
{
    public const string PolicyName = "satellite-token";
    public const string Section = "Auth:Satellite:TokenRateLimit";
    public const int DefaultPermitLimit = 20;
    public const int DefaultWindowSeconds = 60;

    public static SatelliteTokenRateLimitOptions FromConfiguration(IConfiguration configuration)
    {
        var (permitLimit, windowSeconds) = FixedWindowConfiguration.Read(configuration, Section, DefaultPermitLimit, DefaultWindowSeconds);

        return new SatelliteTokenRateLimitOptions(permitLimit, windowSeconds);
    }
}

internal static class FixedWindowConfiguration
{
    public static (int PermitLimit, int WindowSeconds) Read(IConfiguration configuration, string sectionName, int defaultPermitLimit, int defaultWindowSeconds)
    {
        var section = configuration.GetSection(sectionName);
        var permitLimit = Read(section, "PermitLimit", defaultPermitLimit);
        var windowSeconds = Read(section, "WindowSeconds", defaultWindowSeconds);

        if (permitLimit < 1 || windowSeconds < 1)
        {
            throw new InvalidOperationException($"Configuração inválida: '{sectionName}' precisa de PermitLimit e WindowSeconds positivos.");
        }

        return (permitLimit, windowSeconds);
    }

    private static int Read(IConfigurationSection section, string key, int fallback) =>
        int.TryParse(section[key], NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : fallback;

    public static RateLimitPartition<string> PerIp(HttpContext httpContext, int permitLimit, int windowSeconds) =>
        RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = permitLimit,
                Window = TimeSpan.FromSeconds(windowSeconds),
                QueueLimit = 0,
                AutoReplenishment = true
            });
}

public static class LoginRateLimitSetup
{
    public static IServiceCollection AddLoginRateLimit(this IServiceCollection services)
    {
        services.AddSingleton(provider => LoginRateLimitOptions.FromConfiguration(provider.GetRequiredService<IConfiguration>()));
        services.AddSingleton(provider => SatelliteTokenRateLimitOptions.FromConfiguration(provider.GetRequiredService<IConfiguration>()));
        services.AddRateLimiter(options =>
        {
            options.AddPolicy(LoginRateLimitOptions.PolicyName, httpContext =>
            {
                var limits = httpContext.RequestServices.GetRequiredService<LoginRateLimitOptions>();

                return FixedWindowConfiguration.PerIp(httpContext, limits.PermitLimit, limits.WindowSeconds);
            });

            options.AddPolicy(SatelliteTokenRateLimitOptions.PolicyName, httpContext =>
            {
                var limits = httpContext.RequestServices.GetRequiredService<SatelliteTokenRateLimitOptions>();

                return FixedWindowConfiguration.PerIp(httpContext, limits.PermitLimit, limits.WindowSeconds);
            });

            options.OnRejected = async (context, cancellationToken) =>
            {
                var services = context.HttpContext.RequestServices;
                var satellite = context.HttpContext.GetEndpoint()?.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName
                    == SatelliteTokenRateLimitOptions.PolicyName;
                var window = satellite
                    ? services.GetRequiredService<SatelliteTokenRateLimitOptions>().WindowSeconds
                    : services.GetRequiredService<LoginRateLimitOptions>().WindowSeconds;
                var retryAfter = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var wait)
                    ? (int)Math.Ceiling(wait.TotalSeconds)
                    : window;

                context.HttpContext.Response.Headers.RetryAfter = retryAfter.ToString(CultureInfo.InvariantCulture);
                await ApiProblems.WriteAsync(
                    context.HttpContext,
                    StatusCodes.Status429TooManyRequests,
                    satellite ? ErrorCodes.SatelliteTokenRateLimited : ErrorCodes.LoginRateLimited,
                    satellite ? ApiProblems.SatelliteTokenRateLimitedDetail : ApiProblems.LoginRateLimitedDetail);
            };
        });

        return services;
    }
}
