using Makosh.Core;

namespace Makosh.Tests;

public sealed class FakeChatClient : IChatClient
{
    readonly Queue<object> _steps;

    public FakeChatClient(params object[] steps)
    {
        _steps = new Queue<object>(steps);
    }

    public List<LlmRequest> Calls { get; } = [];

    public static LlmResponse Text(string? content) => new() { Content = content };

    public static LlmResponse Tool(string name, string arguments, string callId = "call-1") =>
        Tools((name, arguments, callId));

    public static LlmResponse Tools(params (string Name, string Arguments, string Id)[] calls) =>
        new()
        {
            Content = "",
            ToolCalls = calls.Select(call => new LlmToolCall
            {
                Id = call.Id,
                Name = call.Name,
                Arguments = call.Arguments,
            }).ToList(),
        };

    public Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken)
    {
        Calls.Add(request);
        if (_steps.Count == 0)
        {
            throw new InvalidOperationException("fake LLM got an unexpected call");
        }

        var step = _steps.Dequeue();
        if (step is Exception exception)
        {
            throw exception;
        }

        return Task.FromResult((LlmResponse)step);
    }

    public void Reset(params object[] steps)
    {
        Calls.Clear();
        _steps.Clear();
        foreach (var step in steps)
        {
            _steps.Enqueue(step);
        }
    }
}
