using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Threading.Channels;
using System.Text.Json;

namespace Karar.Api.Services;

public sealed class SseConnectionManager
{
    private readonly ConcurrentDictionary<Guid, ImmutableList<Channel<string>>> _connections = new();

    public Channel<string> Subscribe(Guid postId)
    {
        var channel = Channel.CreateBounded<string>(new BoundedChannelOptions(50)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });

        _connections.AddOrUpdate(
            postId,
            _ => ImmutableList.Create(channel),
            (_, existing) => existing.Add(channel)
        );

        return channel;
    }

    public void Unsubscribe(Guid postId, Channel<string> channel)
    {
        _connections.AddOrUpdate(
            postId,
            _ => ImmutableList<Channel<string>>.Empty,
            (_, existing) => existing.Remove(channel)
        );

        if (_connections.TryGetValue(postId, out var current) && current.Count == 0)
            _connections.TryRemove(new KeyValuePair<Guid, ImmutableList<Channel<string>>>(postId, current));

        channel.Writer.TryComplete();
    }

    public void Broadcast(Guid postId, string eventType, object data)
    {
        if (!_connections.TryGetValue(postId, out var snapshot)) return;

        var payload = $"event: {eventType}\ndata: {JsonSerializer.Serialize(data)}\n\n";
        foreach (var ch in snapshot)
        {
            ch.Writer.TryWrite(payload);
        }
    }

    public int ConnectionCount(Guid postId) =>
        _connections.TryGetValue(postId, out var list) ? list.Count : 0;
}
