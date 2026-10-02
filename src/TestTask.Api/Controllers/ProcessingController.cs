using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using TestTask.Api.Models;
using TestTask.Api.Services;
using TestTask.Api.Validation;

namespace TestTask.Api.Controllers;

[ApiController]
[Route("api/process")]
public sealed class ProcessingController(
    IValidator<ProcessRequest> validator,
    IHtmlProcessingService processingService) : ControllerBase
{
    [HttpPost]
    [Consumes("application/json")]
    [Produces("application/json")]
    [ProducesResponseType(typeof(ProcessResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProcessResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProcessResponse), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(typeof(ProcessResponse), StatusCodes.Status500InternalServerError)]
    [ProducesResponseType(typeof(ProcessResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProcessResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProcessResponse), StatusCodes.Status408RequestTimeout)]
    [ProducesResponseType(typeof(ProcessResponse), StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType(typeof(ProcessResponse), StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<ProcessResponse>> ProcessAsync(
        [FromBody] ProcessRequest? request,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return BadRequest(ProcessResponse.Failure(
                ErrorCodes.ValidationError,
                "The request body is required."));
        }

        var validationResult = await validator.ValidateAsync(request, cancellationToken);

        if (!validationResult.IsValid)
        {
            var failure = validationResult.Errors[0];
            var errorCode = string.IsNullOrWhiteSpace(failure.ErrorCode)
                ? ErrorCodes.ValidationError
                : failure.ErrorCode;

            return new ObjectResult(ProcessResponse.Failure(errorCode, failure.ErrorMessage))
            {
                StatusCode = errorCode == ErrorCodes.LimitExceeded ? 413 : 400
            };
        }

        var response = await processingService.ProcessAsync(request, cancellationToken);
        return ToHttpResult(response);
    }

    private static ActionResult<ProcessResponse> ToHttpResult(ProcessResponse response)
    {
        if (response.IsError == 0)
        {
            return new OkObjectResult(response);
        }

        var statusCode = response.ErrorCode switch
        {
            ErrorCodes.ValidationError or
                ErrorCodes.InvalidRequest or
                ErrorCodes.RequiredSelector or
                ErrorCodes.RequiredAttribute or
                ErrorCodes.RequiredUrlBase64 or
                ErrorCodes.RequiredPageBase64 or
                ErrorCodes.RequiredKeyBase64 or
                ErrorCodes.RequiredEncryptedTextBase64 => StatusCodes.Status400BadRequest,

            ErrorCodes.DatabaseError or ErrorCodes.InternalError =>
                StatusCodes.Status500InternalServerError,

            ErrorCodes.LimitExceeded => StatusCodes.Status413PayloadTooLarge,
            ErrorCodes.RequestTimeout => StatusCodes.Status408RequestTimeout,

            _ => StatusCodes.Status422UnprocessableEntity
        };

        return new ObjectResult(response)
        {
            StatusCode = statusCode
        };
    }
}
