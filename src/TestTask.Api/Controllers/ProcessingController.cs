using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using System.Data.Common;
using TestTask.Api.Models;
using TestTask.Api.Services;
using TestTask.Api.Validation;

namespace TestTask.Api.Controllers;

[ApiController]
[Route("api/process")]
public sealed class ProcessingController(
    IValidator<ProcessRequest> validator,
    IHtmlProcessingService processingService,
    ILogger<ProcessingController> logger) : ControllerBase
{
    [HttpPost]
    [Consumes("application/json")]
    [Produces("application/json")]
    [ProducesResponseType(typeof(ProcessResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProcessResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProcessResponse), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(typeof(ProcessResponse), StatusCodes.Status500InternalServerError)]
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

            return BadRequest(ProcessResponse.Failure(errorCode, failure.ErrorMessage));
        }

        try
        {
            var response = await processingService.ProcessAsync(request, cancellationToken);
            return ToHttpResult(response);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (DbException exception)
        {
            logger.LogError(exception, "Database error while processing request.");
            return StatusCode(
                StatusCodes.Status500InternalServerError,
                ProcessResponse.Failure(
                    ErrorCodes.DatabaseError,
                    $"Database error: {exception.Message}"));
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Unexpected error while processing request.");
            return StatusCode(
                StatusCodes.Status500InternalServerError,
                ProcessResponse.Failure(
                    ErrorCodes.InternalError,
                    $"Internal error: {exception.Message}"));
        }
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

            _ => StatusCodes.Status422UnprocessableEntity
        };

        return new ObjectResult(response)
        {
            StatusCode = statusCode
        };
    }
}
