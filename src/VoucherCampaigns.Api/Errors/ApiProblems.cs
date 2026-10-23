using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace VoucherCampaigns.Api.Errors;

/// <summary>Códigos estáveis da extensão <c>code</c> dos ProblemDetails (contrato com frontend e satélites).</summary>
public static class ErrorCodes
{
    public const string ValidationError = "VALIDATION_ERROR";
    public const string InvalidCredentials = "AUTH_INVALID_CREDENTIALS";
    public const string Unauthenticated = "AUTH_UNAUTHENTICATED";
    public const string ForbiddenRole = "AUTH_FORBIDDEN_ROLE";
    public const string ScopeForbidden = "SCOPE_FORBIDDEN";
    public const string NotFound = "NOT_FOUND";
}

public static class ApiProblems
{
    public const string CodeExtension = "code";

    public const string ValidationDetail = "A requisição contém campos inválidos.";
    public const string InvalidCredentialsDetail = "Credenciais inválidas.";
    public const string UnauthenticatedDetail = "Autenticação necessária.";
    public const string ForbiddenRoleDetail = "Seu papel não permite esta operação.";
    public const string ScopeForbiddenDetail = "Escopo organizacional fora da sua permissão de acesso";

    public static ProblemDetails Create(HttpContext httpContext, int statusCode, string code, string detail)
    {
        var problem = Factory(httpContext).CreateProblemDetails(httpContext, statusCode, detail: detail);
        problem.Extensions[CodeExtension] = code;

        return problem;
    }

    public static ObjectResult Result(HttpContext httpContext, int statusCode, string code, string detail) =>
        new(Create(httpContext, statusCode, code, detail)) { StatusCode = statusCode };

    /// <summary>Resposta 400 de erro de contrato, usada no lugar da resposta padrão do [ApiController].</summary>
    public static IActionResult Validation(ActionContext context)
    {
        var problem = Factory(context.HttpContext).CreateValidationProblemDetails(
            context.HttpContext, context.ModelState, StatusCodes.Status400BadRequest, detail: ValidationDetail);
        problem.Extensions[CodeExtension] = ErrorCodes.ValidationError;

        return new BadRequestObjectResult(problem) { ContentTypes = { "application/problem+json" } };
    }

    /// <summary>Escreve fora do pipeline do MVC (ex.: eventos do JwtBearer).</summary>
    public static async Task WriteAsync(HttpContext httpContext, int statusCode, string code, string detail)
    {
        httpContext.Response.StatusCode = statusCode;

        await httpContext.RequestServices.GetRequiredService<IProblemDetailsService>().WriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = Create(httpContext, statusCode, code, detail)
        });
    }

    private static ProblemDetailsFactory Factory(HttpContext httpContext) =>
        httpContext.RequestServices.GetRequiredService<ProblemDetailsFactory>();
}
