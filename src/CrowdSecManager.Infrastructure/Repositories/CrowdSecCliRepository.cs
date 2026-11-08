using CrowdSecManager.Core.Repositories;
using CrowdsecManagerModel;
using CrowdSecManager.Services;

namespace CrowdSecManager.Infrastructure.Repositories;

/// <summary>Repository adapter exposing safe terminal command execution to ViewModels.</summary>
public sealed class CrowdSecCliRepository(ICrowdSecCliService cli) : ITerminalRepository
{
    public Task<CliResult> ExecuteAsync(string commandLine, CancellationToken ct = default) =>
        cli.RunTerminalAsync(commandLine, ct);
}
