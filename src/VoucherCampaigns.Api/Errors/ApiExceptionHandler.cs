using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using VoucherCampaigns.Application.Authentication;
using VoucherCampaigns.Application.Common;
using VoucherCampaigns.Domain.Common;

namespace VoucherCampaigns.Api.Errors;

/// <summary>
/// Converte exceções esperadas em ProblemDetails: regra de negócio → 422 com o código da regra;
/// erro de contrato detectado no serviço → 400 VALIDATION_ERROR; recurso ausente → 404;
/// rede fora do token → 403. As demais seguem para o 500 padrão.
/// </summary>
public class ApiExceptionHandler(IProblemDetailsService problemDetailsService, ProblemDetailsFactory problemDetailsFactory)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        ProblemDetails? problem = exception switch
        {
            BusinessRuleException rule => ApiProblems.Create(
                httpContext, StatusCodes.Status422UnprocessableEntity, rule.Code, rule.Message),
            RequestValidationException validation => ValidationProblem(httpContext, validation),
            ResourceNotFoundException notFound => ApiProblems.Create(
                httpContext, StatusCodes.Status404NotFound, ErrorCodes.NotFound, notFound.Message),
            ScopeForbiddenException => ApiProblems.Create(
                httpContext, StatusCodes.Status403Forbidden, ErrorCodes.ScopeForbidden, ApiProblems.ScopeForbiddenDetail),
            _ => null
        };

        if (problem is null)
        {
            return false;
        }

        httpContext.Response.StatusCode = problem.Status!.Value;

        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem,
            Exception = exception
        });
    }

    private ValidationProblemDetails ValidationProblem(HttpContext httpContext, RequestValidationException exception)
    {
        var modelState = new ModelStateDictionary();
        modelState.AddModelError(exception.Field, exception.Message);

        var problem = problemDetailsFactory.CreateValidationProblemDetails(
            httpContext, modelState, StatusCodes.Status400BadRequest, detail: ApiProblems.ValidationDetail);
        problem.Extensions[ApiProblems.CodeExtension] = ErrorCodes.ValidationError;

        return problem;
    }
}
