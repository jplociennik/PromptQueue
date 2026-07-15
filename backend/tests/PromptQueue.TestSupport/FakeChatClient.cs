using Microsoft.Extensions.AI;

namespace PromptQueue.TestSupport;

/// <summary>Testowy IChatClient: zachowanie per wywołanie (tekst albo wyjątek), z licznikiem prób. Bez sieci.</summary>
public sealed class FakeChatClient(Func<int, string> respond) : IChatClient
{
    public int CallCount { get; private set; }

    public static FakeChatClient Returning(string text) => new(_ => text);
    public static FakeChatClient Throwing(Exception exception) => new(_ => throw exception);

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        CallCount++;
        return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, respond(CallCount))));
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose() { }
}
