using Runlet.Shared.Workflows;

namespace Runlet.Api.Validation;

public static class CreateWorkflowRunRequestValidator
{
    public static string? Validate(CreateWorkflowRunRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Image))
        {
            return "Workflow image is required.";
        }

        if (request.Steps.Count == 0)
        {
            return "At least one workflow step is required.";
        }

        if (request.Steps.Any(string.IsNullOrWhiteSpace))
        {
            return "Workflow steps cannot be empty.";
        }

        if (request.StepTimeoutSeconds is < 1 or > 86_400)
        {
            return "Step timeout must be between 1 and 86400 seconds.";
        }

        if (request.MaxRetries is < 0 or > 10)
        {
            return "Max retries must be between 0 and 10.";
        }

        if (request.RetryDelaySeconds is < 0 or > 3_600)
        {
            return "Retry delay must be between 0 and 3600 seconds.";
        }

        if (request.Name?.Trim().Length > 200)
        {
            return "Run name cannot be longer than 200 characters.";
        }

        return null;
    }
}
