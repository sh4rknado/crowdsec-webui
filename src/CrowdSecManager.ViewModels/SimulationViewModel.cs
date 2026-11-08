using CrowdSecManager.Services;
using CrowdsecManagerModel;
using Microsoft.Extensions.Logging;

namespace CrowdSecManager.ViewModels;

public sealed class SimulationViewModel : ViewModelBase
{
    private readonly ICrowdSecCliService _cli;
    private readonly ILogger<SimulationViewModel> _log;
    private CsSimulationStatus? _status;
    private IReadOnlyList<CsSimulation> _simulations = [];
    private string _newScenario = "";
    private bool _isBusy;
    private bool _isAdding;
    private string? _message;
    private bool _hasError;

    public SimulationViewModel(ICrowdSecCliService cli, ILogger<SimulationViewModel> log) { _cli = cli; _log = log; }
    public CsSimulationStatus? Status { get => _status; private set => SetProperty(ref _status, value); }
    public IReadOnlyList<CsSimulation> Simulations { get => _simulations; private set => SetProperty(ref _simulations, value); }
    public string NewScenario { get => _newScenario; set => SetProperty(ref _newScenario, value); }
    public bool IsBusy { get => _isBusy; private set => SetProperty(ref _isBusy, value); }
    public bool IsAdding { get => _isAdding; private set => SetProperty(ref _isAdding, value); }
    public string? Message { get => _message; private set => SetProperty(ref _message, value); }
    public bool HasError { get => _hasError; private set => SetProperty(ref _hasError, value); }

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
