using CrowdSecManager.Services;
using CrowdsecManagerModel;
using Microsoft.Extensions.Logging;

namespace CrowdSecManager.ViewModels;

public sealed class MachinesViewModel(ICrowdSecCliService cli, ILogger<MachinesViewModel> log) : ViewModelBase
{
    private readonly ICrowdSecCliService _cli = cli;
    private readonly ILogger<MachinesViewModel> _log = log;

    public string NewMachine { get; private set => SetProperty(ref field, value); } = "";
    public bool AutoValidate { get; private set => SetProperty(ref field, value); }
    public bool IsBusy { get; private set => SetProperty(ref field, value); }
    public bool IsAdding { get; private set => SetProperty(ref field, value); }
    public string? Message { get; private set => SetProperty(ref field, value); }
    public bool HasError { get; private set => SetProperty(ref field, value); }
    public string? RegisterOutput { get; private set => SetProperty(ref field, value); }

    public IReadOnlyList<CsMachine> Machines { get; private set => SetProperty(ref field, value); } = [];

    public override Task InitializeAsync() => RefreshAsync();

    public async Task RefreshAsync()
    {
        try { Machines = await _cli.ListMachinesAsync(); }
        catch (Exception ex) { _log.LogDebug("machines: {Msg}", ex.Message); }
    }

    public async Task RegisterAsync()
    {
        IsAdding = true; Message = null; RegisterOutput = null;
        try
        {
            var result = await _cli.RegisterMachineAsync(string.IsNullOrWhiteSpace(NewMachine) ? null : NewMachine.Trim(), AutoValidate);
            HasError = !result.Success;
            RegisterOutput = result.Success
                ? result.Stdout + (string.IsNullOrWhiteSpace(result.Stderr) ? "" : "\n--- stderr ---\n" + result.Stderr)
                : $"Exit code {result.ExitCode}\n\n{result.FlattenError() ?? result.Stderr}";
            Message = result.Success
                ? "Machine registered. Credentials are written to local_api_credentials.yaml on the machine that ran cscli."
                : $"Failed: {result.FlattenError() ?? result.Stderr}";
            if (result.Success) { NewMachine = ""; AutoValidate = false; await RefreshAsync(); }
        }
        finally { IsAdding = false; }
    }

    public async Task DeleteAsync(string machineId)
    {
        var result = await _cli.DeleteMachineAsync(machineId);
        HasError = !result.Success;
        Message = result.Success ? $"Machine '{machineId}' deleted." : $"Failed: {result.FlattenError() ?? result.Stderr}";
        await RefreshAsync();
    }

    public async Task PruneAsync()
    {
        IsBusy = true; Message = null;
        try
        {
            var result = await _cli.PruneMachinesAsync();
            HasError = !result.Success;
            Message = result.Success ? "Pruned expired machines." : $"Failed: {result.FlattenError() ?? result.Stderr}";
            await RefreshAsync();
        }
        finally { IsBusy = false; }
    }
}
