using System.Collections.Concurrent;
using Agpme.Bbg.Stream.Playground.Client.Configuration;
using Agpme.Bbg.Stream.Playground.Client.Models;
using Microsoft.Extensions.Options;

namespace Agpme.Bbg.Stream.Playground.Client.Services;

public sealed class SubscriptionManager : ISubscriptionManager
{
    private readonly PlaygroundClientOptions _opts;
    private readonly IHttpClientFactory _httpFactory;
    private readonly IPositionInboundPersister _persister;
    private readonly ConcurrentDictionary<SubscriptionKey, (Task Task, CancellationTokenSource Cts, SubscriptionStatus Status)> _running = new();


    public SubscriptionManager(
        IOptions<PlaygroundClientOptions> opts,
        IHttpClientFactory httpFactory,
        IPositionInboundPersister persister)
    {
        _opts = opts.Value;
        _httpFactory = httpFactory;
        _persister = persister;
    }


    public async Task StartAllConfiguredAsync(CancellationToken ct)
    {
        foreach (var t in _opts.Targets)
        {
            var key = new SubscriptionKey(t.entityType, t.entityName);
            _ = await StartAsync(key, ct);
        }
    }

    public IReadOnlyCollection<SubscriptionStatus> List()
        => _running.Values.Select(v => v.Status).ToArray();

    public SubscriptionStatus? Get(SubscriptionKey key)
        => _running.TryGetValue(key, out var v) ? v.Status : null;

    public async Task<SubscriptionStatus> StartAsync(SubscriptionKey key, CancellationToken ct)
    {
        if (_running.ContainsKey(key))
            return _running[key].Status;

        var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var status = new SubscriptionStatus
        {
            Key = key,
            Metrics = { StartedAt = DateTimeOffset.UtcNow, State = SubscriptionState.Starting }
        };


        // Isolated logger creation
        var subLogger = SubscriptionLogging.CreateSubscriptionLogger(key.entityType, key.entityName);


        var client = _httpFactory.CreateClient("playground");

        var task = StreamConsumer.RunAsync(
            client, _opts, key, status.Metrics,
            subLogger,               // your per-subscription logger (already added)
            _persister,              // <-- pass the persister to the consumer
            cts.Token);


        _running[key] = (task, cts, status);

        _ = task.ContinueWith(t =>
        {
            try
            {
                if (t.IsFaulted)
                {
                    status.Metrics.LastError = t.Exception?.GetBaseException().Message;
                    status.Metrics.State = SubscriptionState.Error;
                }
                else
                {
                    status.Metrics.State = SubscriptionState.Stopped;
                }
                status.Metrics.StoppedAt = DateTimeOffset.UtcNow;
                _running.TryRemove(key, out _);
            }
            finally
            {
                (subLogger as IDisposable)?.Dispose();
            }

        }, TaskScheduler.Default);

        await Task.Yield();
        return status;
    }

    public Task<bool> StopAsync(SubscriptionKey key)
    {
        if (_running.TryRemove(key, out var v))
        {
            v.Cts.Cancel();
            return Task.FromResult(true);
        }
        return Task.FromResult(false);
    }
}