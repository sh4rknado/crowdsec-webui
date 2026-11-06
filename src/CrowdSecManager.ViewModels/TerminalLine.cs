using System.ComponentModel;
using CrowdSecManager.Core.CommandLine;
using CrowdSecManager.Core.Repositories;
using CrowdSecManager.Models;
using Microsoft.Extensions.Logging;

namespace CrowdSecManager.ViewModels;

/// <summary>
/// One rendered line in the terminal output. Kept as a readonly record so
/// the view can iterate cheaply and tests can assert on it precisely.
/// </summary>
public sealed record TerminalLine(string Text, string Class, bool IsPrompt)
{
    public static TerminalLine Cmd(string text) => new(text, "cmd", true);
    public static TerminalLine Out(string text) => new(text, "out", false);
    public static TerminalLine Err(string text) => new(text, "err", false);
    public static TerminalLine Muted(string text) => new(text, "muted", false);
}
