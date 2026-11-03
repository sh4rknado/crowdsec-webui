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

/// <summary>
/// Presentation state + logic for the interactive cscli terminal page (MVVM).
///
/// The view binds to this VM and re-renders on
/// <see cref="INotifyPropertyChanged"/>; every cscli call is offloaded via
/// <see cref="Task.Run"/> so the Blazor circuit never blocks. All terminal
/// state lives here: the lines, the input buffer, the history with its
/// cursor, the busy flag and the per-command cancellation token.
///
/// The VM talks only to <see cref="ITerminalRepository"/>; it never spawns
/// cscli directly. Command-line tokenization is delegated to
/// <see cref="CommandLineTokenizer"/> (in Infrastructure), which is also
/// used by the CLI service so the behavior stays consistent.
/// </summary>
public sealed class TerminalViewModel : ViewModelBase, IDisposable
{
    private readonly ITerminalRepository _repo;
    private readonly ILogger<TerminalViewModel> _log;

    // Backing state
    private readonly List<TerminalLine> _lines = new();
    private readonly List<string> _history = new();
    private string _input = "";
    private bool _busy;
    private int _historyCursor = -1;   // -1 = not browsing history
    private CancellationTokenSource? _cts;
    private int _pendingCount;          // number of in-flight Execute calls (race guard)

    public TerminalViewModel(ITerminalRepository repo, ILogger<TerminalViewModel> log)
    {
        _repo = repo;
        _log = log;
        // Initial banner so the page is never empty on first paint.
        _lines.Add(TerminalLine.Muted("CrowdSec CLI terminal. Type 'help' for common commands, 'clear' to wipe the screen."));
        _lines.Add(TerminalLine.Muted("Everything runs as cscli <your input>. No shell, no pipes, no redirection."));
    }

    // ---- Observable state ----

    /// <summary>Immutable snapshot of the rendered lines for the view.</summary>
    public IReadOnlyList<TerminalLine> Lines
    {
        get => _lines;
        private set
        {
            _lines.Clear();
            if (value is not null) _lines.AddRange(value);
            RaiseComputedPropertyChanged(nameof(Lines));
        }
    }

    public string Input
    {
        get => _input;
        set => SetProperty(ref _input, value ?? "");
    }

    public bool IsBusy
    {
        get => _busy;
        private set => SetProperty(ref _busy, value);
    }

    public bool HasHistory => _history.Count > 0;

    // ---- Commands ----

    /// <summary>
    /// Run whatever is currently in <see cref="Input"/>. Convenience overload
    /// for buttons / non-Enter triggers. The Enter key handler should prefer
    /// <see cref="ExecuteAsync(string)"/> and pass the just-captured input,
    /// so the VM never depends on a possibly-stale @bind round-trip.
    /// </summary>
    public Task ExecuteAsync() => ExecuteAsync(Input);

    /// <summary>
    /// Run <paramref name="rawInput"/> as a cscli command line. The input
    /// buffer is cleared immediately, the prompt line is echoed, and
    /// built-in commands (<c>help</c>, <c>clear</c>, <c>history</c>) are
    /// handled locally without touching cscli. Anything else is forwarded
    /// to the repository as an already-tokenized argv (no shell).
    /// </summary>
    public async Task ExecuteAsync(string rawInput)
    {
        var raw = (rawInput ?? "").Trim();
        // Clear the visible input field right away so the user gets visual
        // feedback that their Enter was acknowledged, even before the
        // command result arrives.
        Input = "";
        if (string.IsNullOrWhiteSpace(raw)) return;

        // Echo the prompt line first so the user gets immediate feedback.
        AddLine(TerminalLine.Cmd(raw));

        var firstToken = CommandLineTokenizer.FirstTokenLower(raw);

        if (firstToken == "clear")
        {
            // Match the Clear() button behaviour so "clear" and the button
            // are interchangeable: wipe the screen AND leave a single muted
            // marker line, otherwise the page would render an empty box.
            Clear();
            return;
        }
        if (firstToken == "help")
        {
            ShowHelp();
            return;
        }
        if (firstToken == "history")
        {
            ShowHistory();
            return;
        }

        // Record in history (skip duplicates of the last entry).
        if (_history.Count == 0 || _history[^1] != raw)
            _history.Add(raw);
        _historyCursor = -1;
        RaiseComputedPropertyChanged(nameof(HasHistory));

        // Re-entrancy guard: the input is disabled while busy, but if a
        // previous Execute is still winding down (cancellation race), we
        // wait for it to settle before starting a new one. This is the
        // bug that used to make the terminal appear "frozen": the old
        // finally block would set _busy=false AFTER the new command had
        // already set it to true.
        while (_pendingCount > 0)
            await Task.Delay(10);

        _pendingCount++;
        IsBusy = true;
        CancelPreviousCommand();
        _cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));

        try
        {
            var r = await Task.Run(() => _repo.ExecuteAsync(raw, _cts.Token));

            // Build the output text the same way a real terminal would:
            // stdout first, then stderr on its own line, with a marker for
            // the exit code when the command failed.
            var sb = new System.Text.StringBuilder();
            if (!string.IsNullOrEmpty(r.Stdout)) sb.Append(r.Stdout);
            if (!string.IsNullOrWhiteSpace(r.Stderr))
            {
                if (sb.Length > 0 && !sb.ToString().EndsWith('\n')) sb.Append('\n');
                sb.Append(r.Stderr);
            }
            if (sb.Length == 0)
                sb.Append($"(no output, exit code {r.ExitCode})");
            else if (r.ExitCode != 0)
                sb.Append($"\n[exit code {r.ExitCode}]");

            var text = sb.ToString().TrimEnd('\r', '\n');
            // Split multi-line output so each terminal line wraps independently.
            foreach (var l in text.Split('\n'))
                AddLine(r.ExitCode == 0 ? TerminalLine.Out(l) : TerminalLine.Err(l));
        }
        catch (OperationCanceledException)
        {
            AddLine(TerminalLine.Err("[command timed out after 5 minutes]"));
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Terminal command failed: {Cmd}", raw);
            AddLine(TerminalLine.Err($"[error] {ex.Message}"));
        }
        finally
        {
            _pendingCount--;
            // Only clear the busy flag when no other Execute is queued
            // behind us. This is the second half of the race fix.
            if (_pendingCount == 0)
                IsBusy = false;
        }
    }

    /// <summary>
    /// Navigate the history buffer by delta (-1 = older, +1 = newer).
    /// Bound to Up / Down arrow keys. Mutates <see cref="Input"/> and the
    /// internal cursor; safe to call when the history is empty (no-op).
    /// </summary>
    public void BrowseHistory(int delta)
    {
        if (_history.Count == 0) return;
        if (_historyCursor < 0)
        {
            // Start browsing from the last entry (only meaningful on ArrowUp).
            if (delta < 0) _historyCursor = _history.Count - 1;
            else return;
        }
        else
        {
            _historyCursor += delta;
            if (_historyCursor >= _history.Count) { _historyCursor = -1; Input = ""; return; }
            if (_historyCursor < 0) { _historyCursor = -1; Input = ""; return; }
        }
        Input = _history[_historyCursor];
    }

    /// <summary>Print the static help text (local command, no cscli call).</summary>
    public void ShowHelp()
    {
        foreach (var l in HelpText.Split('\n'))
            AddLine(TerminalLine.Out(l));
    }

    /// <summary>Print the current history buffer with line numbers.</summary>
    public void ShowHistory()
    {
        if (_history.Count == 0)
        {
            AddLine(TerminalLine.Muted("(history is empty)"));
            return;
        }
        AddLine(TerminalLine.Out(string.Join('\n',
            _history.Select((c, i) => $"  {i + 1,3}  {c}"))));
    }

    /// <summary>Wipe the screen and leave a single muted marker line.</summary>
    public void Clear()
    {
        ClearLines();
        AddLine(TerminalLine.Muted("(screen cleared)"));
    }

    /// <summary>
    /// Cancel any in-flight command (used by Dispose when the Blazor circuit
    /// is torn down, and available as a button handler).
    /// </summary>
    public void Cancel()
    {
        try { _cts?.Cancel(); }
        catch (ObjectDisposedException) { /* already gone */ }
        catch (Exception ex) { _log.LogDebug(ex, "Terminal cancel ignored"); }
    }

    public void Dispose()
    {
        Cancel();
        _cts?.Dispose();
        _cts = null;
    }

    // ---- Internal helpers ----

    private void AddLine(TerminalLine line)
    {
        _lines.Add(line);
        RaiseComputedPropertyChanged(nameof(Lines));
    }

    private void ClearLines()
    {
        _lines.Clear();
        RaiseComputedPropertyChanged(nameof(Lines));
    }

    private void CancelPreviousCommand()
    {
        var previous = _cts;
        if (previous is null) return;
        try { previous.Cancel(); }
        catch (ObjectDisposedException) { /* already gone */ }
        catch (Exception ex) { _log.LogDebug(ex, "Previous cancel ignored"); }
    }

    /// <summary>Static help text exposed so tests can assert on it.</summary>
    public static string HelpText { get; } = string.Join('\n', new[]
    {
        "Common cscli commands:",
        "  decisions list                       # list active bans",
        "  decisions add -i 1.2.3.4 -d 4h       # ban an IP for 4h",
        "  decisions delete -i 1.2.3.4          # remove a ban",
        "  alerts list                          # recent alerts",
        "  bouncers list                        # registered bouncers",
        "  machines list                        # registered machines",
        "  hub update                           # refresh hub index",
        "  hub upgrade                          # upgrade all hub items",
        "  collections list                     # installed collections",
        "  metrics                              # engine metrics",
        "  config show                          # merged configuration",
        "  config backup /tmp/backup.tgz        # backup config",
        "  lapi status                          # LAPI health",
        "  explain --file /var/log/nginx/access.log --type nginx",
        "  version                              # cscli + crowdsec version",
        "",
        "Add -o json to any list command for machine-readable output.",
        "",
        "Local commands: help, clear, history. Up/Down arrows recall history."
    });
}
