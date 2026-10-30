using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using VoucherCampaigns.Api.Errors;
using VoucherCampaigns.Application.Authentication;

namespace VoucherCampaigns.Api.Filters;

/// <summary>
/// TenantGuard: recusa a operação quando a rede dela não é a rede do usuário (US-02.5).
/// A rede vem do argumento <c>networkId</c> (rota ou query) ou de um argumento <see cref="INetworkScopedRequest"/>.
/// O administrador da plataforma não tem rede e passa direto.
/// </summary>
public class ValidateTenantScopeAttribute : ActionFilterAttribute
{
    public const string NetworkIdArgument = "networkId";

    public override void OnActionExecuting(ActionExecutingContext context)
    {
        if (context.HttpContext.User.Identity?.IsAuthenticated != true)
        {
            context.Result = new ChallengeResult();
            return;
        }

        var currentUser = context.HttpContext.RequestServices.GetRequiredService<ICurrentUser>();

        // O administrador da plataforma não pertence a uma rede (PR-15); as policies decidem o que ele alcança.
        if (currentUser.IsPlatformAdmin)
        {
            return;
        }

        var requestedNetworks = RequestedNetworks(context);

        if (requestedNetworks.Count == 0)
        {
            // Erro de programação: sem rede identificável, liberar o acesso esconderia a falha.
            throw new InvalidOperationException(
                $"{nameof(ValidateTenantScopeAttribute)} exige um argumento '{NetworkIdArgument}' ou um {nameof(INetworkScopedRequest)} " +
                $"na ação '{context.ActionDescriptor.DisplayName}'.");
        }

        // Guid vazio é erro de contrato, não outra rede: o serviço responde 400 (InputValues.RequiredId).
        if (requestedNetworks.Contains(Guid.Empty))
        {
            return;
        }

        if (requestedNetworks.Any(networkId => networkId != currentUser.NetworkId))
        {
            context.Result = ApiProblems.Result(
                context.HttpContext, StatusCodes.Status403Forbidden, ErrorCodes.ScopeForbidden, ApiProblems.ScopeForbiddenDetail);
        }
    }

    private static List<Guid> RequestedNetworks(ActionExecutingContext context)
    {
        var networks = context.ActionArguments.Values.OfType<INetworkScopedRequest>()
            .Select(request => request.NetworkId)
            .ToList();

        if (context.ActionArguments.TryGetValue(NetworkIdArgument, out var argument) && argument is Guid networkId)
        {
            networks.Add(networkId);
        }

        return networks;
    }
}
