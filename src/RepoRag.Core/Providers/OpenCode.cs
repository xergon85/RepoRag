using System.ClientModel;
using System.ClientModel.Primitives;
using OpenAI;

namespace RepoRag.Core.Providers;

/// <summary>
/// OpenCode Go speaks the OpenAI API. The working setup from 25 Nov:
/// this endpoint, plus an x-opencode-session header on every request.
/// </summary>
public static class OpenCode
{
    public static readonly Uri Endpoint = new("https://opencode.ai/inference/go/openai/v1");

    /// <param name="sessionId">One per conversation (per run in the console, per chat in the API).</param>
    public static OpenAIClient CreateClient(string apiKey, string? sessionId = null)
    {
        var options = new OpenAIClientOptions { Endpoint = Endpoint, UserAgentApplicationId = "RepoRag/0.1" };
        options.AddPolicy(new SessionHeaderPolicy(sessionId ?? Guid.NewGuid().ToString()), PipelinePosition.PerCall);
        return new OpenAIClient(new ApiKeyCredential(apiKey), options);
    }

    /// <summary>Sets the session header, then passes the request on to the next policy in the SDK's pipeline.</summary>
    private sealed class SessionHeaderPolicy(string sessionId) : PipelinePolicy
    {
        public override void Process(PipelineMessage message, IReadOnlyList<PipelinePolicy> pipeline, int currentIndex)
        {
            message.Request.Headers.Set("x-opencode-session", sessionId);
            ProcessNext(message, pipeline, currentIndex);
        }

        public override ValueTask ProcessAsync(PipelineMessage message, IReadOnlyList<PipelinePolicy> pipeline, int currentIndex)
        {
            message.Request.Headers.Set("x-opencode-session", sessionId);
            return ProcessNextAsync(message, pipeline, currentIndex);
        }
    }
}
