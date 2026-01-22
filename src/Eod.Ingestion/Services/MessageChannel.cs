using System.Threading.Channels;

namespace Eod.Ingestion.Services;

/// <summary>
/// Shared channel for receiving raw FIX messages.
/// Registered as a singleton to allow both IngestionService and FixSimulatorService to use it.
/// </summary>
public sealed class MessageChannel
{
    private readonly Channel<RawFixMessage> _channel;

    public MessageChannel()
    {
        // Bounded channel prevents memory explosion under load
        _channel = Channel.CreateBounded<RawFixMessage>(
            new BoundedChannelOptions(50000)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = false
            });
    }

    public ChannelWriter<RawFixMessage> Writer => _channel.Writer;
    public ChannelReader<RawFixMessage> Reader => _channel.Reader;

    /// <summary>
    /// Enqueues a raw message for processing.
    /// </summary>
    public async ValueTask EnqueueAsync(RawFixMessage message, CancellationToken ct = default)
    {
        await _channel.Writer.WriteAsync(message, ct);
    }

    /// <summary>
    /// Returns an async enumerable of all messages.
    /// </summary>
    public IAsyncEnumerable<RawFixMessage> ReadAllAsync(CancellationToken ct = default)
    {
        return _channel.Reader.ReadAllAsync(ct);
    }

    public int Count => _channel.Reader.Count;
}
