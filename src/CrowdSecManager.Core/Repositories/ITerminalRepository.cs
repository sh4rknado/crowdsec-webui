using CrowdsecManagerModel;

namespace CrowdSecManager.Core.Repositories;

/// <summary>Executes a terminal command and returns its captured output.</summary>
public interface ITerminalRepository
{
    Task<CliResult> ExecuteAsync(string commandLine, CancellationToken ct = default);
}
