using CrowdSecManager.Services;
using CrowdsecManagerModel;
using Microsoft.Extensions.Logging;

namespace CrowdSecManager.ViewModels;

public sealed class SimulationViewModel(ICrowdSecCliService cli, ILogger<SimulationViewModel> log) : ViewModelBase
{
    private readonly ICrowdSecCliService _cli = cli;
    private readonly ILogger<SimulationViewModel> _log = log;

    public CsSimulationStatus? Status { get; private set => SetProperty(ref field, value); }
    public IReadOnlyList<CsSimulation> Simulations { get; private set => SetProperty(ref field, value); } = [];
    public string NewScenario { get; private set => SetProperty(ref field, value); } = "";
    public bool IsBusy { get; private set => SetProperty(ref field, value); }
    public bool IsAdding { get; private set => SetProperty(ref field, value); }
    public string? Message { get; private set => SetProperty(ref field, value); }
    public bool HasError { get; private set => SetProperty(ref field, value); }

    public override Task InitializeAsync() => RefreshAsync();

    public async Task RefreshAsync()
    {
        try
        {
            Status = await _cli.GetSimulationStatusAsync();
            Simulations = await _cli.ListSimulationsAsync();
        }
        catch (Exception ex) { _log.LogDebug("simulation: {Msg}", ex.Message); }
    }

    public async Task EnableAsync()
    {
        IsBusy = true; Message = null;
        try
        {
            var result = await _cli.EnableSimulationAsync();
            HasError = !result.Success;
            Message = result.Success ? "Simulation enabled globally. Decisions will be recorded but not enforced." : $"Failed: {result.FlattenError() ?? result.Stderr}";
            await RefreshAsync();
        }
        finally { IsBusy = false; }
    }

    public async Task DisableAsync()
    {
        IsBusy = true; Message = null;
        try
        {
            var result = await _cli.DisableSimulationAsync();
            HasError = !result.Success;
            Message = result.Success ? "Simulation disabled. Decisions are now enforced." : $"Failed: {result.FlattenError() ?? result.Stderr}";
            await RefreshAsync();
        }
        finally { IsBusy = false; }
    }

    public async Task AddAsync()
    {
        IsAdding = true; Message = null;
        try
        {
            if (string.IsNullOrWhiteSpace(NewScenario)) { HasError = true; Message = "Scenario name is required."; return; }
            var result = await _cli.AddSimulationAsync(NewScenario.Trim());
            HasError = !result.Success;
            Message = result.Success ? $"Scenario '{NewScenario}' added to simulation." : $"Failed: {result.FlattenError() ?? result.Stderr}";
            if (result.Success) { NewScenario = ""; await RefreshAsync(); }
        }
        finally { IsAdding = false; }
    }

    public async Task RemoveAsync(string scenario)
    {
        var result = await _cli.RemoveSimulationAsync(scenario);
        HasError = !result.Success;
        Message = result.Success ? $"Scenario '{scenario}' removed from simulation." : $"Failed: {result.FlattenError() ?? result.Stderr}";
        await RefreshAsync();
    }
}
