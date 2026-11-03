using EclipsVault.Core.Domain.Exceptions;
using EclipsVault.Web.Models.Ui;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace EclipsVault.Web.Controllers.Ui;

public static class AdministratorProblems
{
    public static JsonResult Problem(int status, string code, string title, string? proof = null)
        => new(new AdministratorProblemResponse { Status = status, Code = code, Title = title, Proof = proof })
        {
            StatusCode = status,
            ContentType = "application/problem+json"
        };

    public static JsonResult UserNotFound() => Problem(StatusCodes.Status404NotFound, "user_not_found", "User not found.");
}

public sealed class AdministratorProblemFilter : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        context.Result = context.Exception switch
        {
            OwnAccountException refused => AdministratorProblems.Problem(StatusCodes.Status409Conflict, "own_account", refused.Message),
            LastAdministratorException refused => AdministratorProblems.Problem(StatusCodes.Status409Conflict, "last_administrator", refused.Message),
            AccountChangedException => AdministratorProblems.Problem(StatusCodes.Status409Conflict, "account_changed",
                "The account was changed by another request. Reload the page and try again."),
            _ => null
        };
        context.ExceptionHandled = context.Result is not null;
    }
}
