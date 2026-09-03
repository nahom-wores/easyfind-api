using EasyFind.Api.Models.Dto.Common;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace EasyFind.Api;

// Runs any registered FluentValidation validator against the action's arguments
// before the action executes.
//
// The validators existed for months but were never registered or called, so the
// conditional rules — a job listing needing a category, a deadline not being in
// the past, salary max >= min — were simply not enforced. Only the DataAnnotation
// attributes on the DTOs were doing anything.
//
// A DTO with no registered validator passes straight through, so adding one is
// opt-in: write the validator, register it in AddLifetimeServices, done.
public class ValidationFilter(IServiceProvider services) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(
        ActionExecutingContext context, ActionExecutionDelegate next)
    {
        foreach (var argument in context.ActionArguments.Values)
        {
            if (argument is null) continue;

            var validatorType = typeof(IValidator<>).MakeGenericType(argument.GetType());
            if (services.GetService(validatorType) is not IValidator validator) continue;

            var result = await validator.ValidateAsync(
                new ValidationContext<object>(argument), context.HttpContext.RequestAborted);

            if (result.IsValid) continue;

            var response = new ApiResponse { IsSuccess = false };
            foreach (var failure in result.Errors)
                response.Errors.Add(failure.ErrorMessage);

            context.Result = new BadRequestObjectResult(response);
            return;
        }

        await next();
    }
}
