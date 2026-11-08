using System.Reflection;
using System.Runtime.ExceptionServices;
using CrowdsecManagerModel;
using CrowdSecManager.Services;
using Microsoft.JSInterop;

namespace CrowdSecManager.ViewModels.Tests;

public class ServiceProxy : DispatchProxy
{
    public Func<string, object?[]?, object?> Handler { get; set; } = (_, _) => null;

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        try
        {
            return Handler(targetMethod!.Name, args);
        }
        catch (Exception exception)
        {
            ExceptionDispatchInfo.Capture(exception).Throw();
            throw;
        }
    }

    public static T Create<T>(Func<string, object?[]?, object?> handler) where T : class
    {
        var proxy = DispatchProxy.Create<T, ServiceProxy>();
        ((ServiceProxy)(object)proxy).Handler = handler;
        return proxy;
    }
}

internal sealed class FakeThreatMapEventBus : IThreatMapEventBus
{
    public event Action<ThreatEvent, ThreatMapStats>? ThreatSpotted;
    public event Action<ThreatMapStats>? StatsUpdated;
    public ThreatMapEventSnapshot Snapshot { get; set; } = new([], new());

    public void PublishThreat(ThreatEvent threat, ThreatMapStats stats) => ThreatSpotted?.Invoke(threat, stats);
    public void PublishStats(ThreatMapStats stats) => StatsUpdated?.Invoke(stats);
    public ThreatMapEventSnapshot GetSnapshot() => Snapshot;
}

internal sealed class FakeJsRuntime : IJSRuntime
{
    public List<string> Calls { get; } = [];

    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
    {
        Calls.Add(identifier);
        return ValueTask.FromResult(default(TValue)!);
    }

    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
    {
        Calls.Add(identifier);
        return ValueTask.FromResult(default(TValue)!);
    }
}
