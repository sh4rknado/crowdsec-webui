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

public interface ICrowdSecProcessRunner
{
    Task<CliResult> ExecuteAsync(ProcessStartInfo startInfo, int timeoutSeconds, CancellationToken ct = default);
}
