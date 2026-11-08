using CrowdSecManager.Services;
using CrowdsecManagerModel;
using Microsoft.Extensions.Logging;

namespace CrowdSecManager.ViewModels;

public sealed class MachinesViewModel : ViewModelBase
{
    private readonly ICrowdSecCliService _cli;
    private readonly ILogger<MachinesViewModel> _log;
    private IReadOnlyList<CsMachine> _machines = [];
    private string _newMachine = "";
    private bool _autoValidate;
    private bool _isBusy;
    private bool _isAdding;
    private string? _message;
    private bool _hasError;
    private string? _registerOutput;

    public MachinesViewModel(ICrowdSecCliService cli, ILogger<MachinesViewModel> log) { _cli = cli; _log = log; }

    public IReadOnlyList<CsMachine> Machines { get => _machines; private set => SetProperty(ref _machines, value); }
    public string NewMachine { get => _newMachine; set => SetProperty(ref _newMachine, value); }
    public bool AutoValidate { get => _autoValidate; set => SetProperty(ref _autoValidate, value); }
    public bool IsBusy { get => _isBusy; private set => SetProperty(ref _isBusy, value); }
    public bool IsAdding { get => _isAdding; private set => SetProperty(ref _isAdding, value); }
    public string? Message { get => _message; private set => SetProperty(ref _message, value); }
    public bool HasError { get => _hasError; private set => SetProperty(ref _hasError, value); }
    public string? RegisterOutput { get => _registerOutput; private set => SetProperty(ref _registerOutput, value); }

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
