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
        var section = configuration.GetSection(Section);
        var options = new LoginRateLimitOptions(
            Read(section, nameof(PermitLimit), DefaultPermitLimit),
            Read(section, nameof(WindowSeconds), DefaultWindowSeconds));

        if (options.PermitLimit < 1 || options.WindowSeconds < 1)
        {
            throw new InvalidOperationException($"Configuração inválida: '{Section}' precisa de PermitLimit e WindowSeconds positivos.");
        }

        return options;
    }

    private static int Read(IConfigurationSection section, string key, int fallback) =>
        int.TryParse(section[key], NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : fallback;
}

public static class LoginRateLimitSetup
{
    public static IServiceCollection AddLoginRateLimit(this IServiceCollection services)
    {
        services.AddSingleton(provider => LoginRateLimitOptions.FromConfiguration(provider.GetRequiredService<IConfiguration>()));
        services.AddRateLimiter(options =>
        {
            options.AddPolicy(LoginRateLimitOptions.PolicyName, httpContext =>
            {
                var limits = httpContext.RequestServices.GetRequiredService<LoginRateLimitOptions>();

                return RateLimitPartition.GetFixedWindowLimiter(
                    httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = limits.PermitLimit,
                        Window = TimeSpan.FromSeconds(limits.WindowSeconds),
                        QueueLimit = 0,
                        AutoReplenishment = true
                    });
            });

            options.OnRejected = async (context, cancellationToken) =>
            {
                var limits = context.HttpContext.RequestServices.GetRequiredService<LoginRateLimitOptions>();
                var retryAfter = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var wait)
                    ? (int)Math.Ceiling(wait.TotalSeconds)
                    : limits.WindowSeconds;

                context.HttpContext.Response.Headers.RetryAfter = retryAfter.ToString(CultureInfo.InvariantCulture);
                await ApiProblems.WriteAsync(
                    context.HttpContext, StatusCodes.Status429TooManyRequests, ErrorCodes.LoginRateLimited, ApiProblems.LoginRateLimitedDetail);
            };
        });

        return services;
    }
}
