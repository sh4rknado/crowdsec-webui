using CrowdSecManager.Core.Repositories;
using CrowdsecManagerModel;
using CrowdSecManager.Services;

namespace CrowdSecManager.Infrastructure.Repositories;

/// <summary>Repository adapter exposing safe terminal command execution to ViewModels.</summary>
public sealed class CrowdSecCliRepository(ICrowdSecCliService cli) : ITerminalRepository
{
    private readonly ICrowdSecCliService _cli = cli ?? throw new ArgumentNullException(nameof(cli));

    public Task<CliResult> ExecuteAsync(string commandLine, CancellationToken ct = default) =>
        _cli.RunTerminalAsync(commandLine, ct);
}
