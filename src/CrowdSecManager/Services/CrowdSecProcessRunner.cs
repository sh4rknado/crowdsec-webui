using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using CrowdSecManager.Core.CommandLine;
using CrowdSecManager.Models;
using Microsoft.Extensions.Options;

namespace CrowdSecManager.Services;

public sealed class CrowdSecProcessRunner : ICrowdSecProcessRunner
{
    public async Task<CliResult> ExecuteAsync(ProcessStartInfo startInfo, int timeoutSeconds,
        CancellationToken ct = default)
    {
        var stopwatch = Stopwatch.StartNew();
        using var process = new Process { StartInfo = startInfo };
        try
        {
            if (!process.Start())
                return new CliResult { ExitCode = -1, Stderr = "Failed to start cscli", Duration = stopwatch.Elapsed };
        }
        catch (Exception ex)
        {
            return new CliResult
            {
                ExitCode = -1,
                Stderr = $"Failed to start cscli: {ex.Message}",
                Duration = stopwatch.Elapsed
            };
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = process.StandardError.ReadToEndAsync(ct);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds > 0 ? timeoutSeconds : 120));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            var message = ct.IsCancellationRequested ? "cscli command cancelled." : "cscli timed out.";
            return new CliResult { ExitCode = -1, Stderr = message, Duration = stopwatch.Elapsed };
        }

        return new CliResult
        {
            ExitCode = process.ExitCode,
            Stdout = await stdoutTask,
            Stderr = await stderrTask,
            Duration = stopwatch.Elapsed
        };
    }
}
