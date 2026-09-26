using TestTask.Api.Models;

namespace TestTask.Api.Services;

public interface IHtmlProcessingService
{
    Task<ProcessResponse> ProcessAsync(
        ProcessRequest request,
        CancellationToken cancellationToken);
}
