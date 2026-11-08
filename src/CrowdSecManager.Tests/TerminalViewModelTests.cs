using System.ComponentModel;
using CrowdSecManager.Core.Repositories;
using CrowdsecManagerModel;
using CrowdSecManager.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CrowdSecManager.Tests;

/// <summary>
/// Unit tests for <see cref="TerminalViewModel"/> - the MVVM presentation
/// layer for the interactive cscli terminal page. Covers:
///   - initial banner state
///   - built-in local commands (help / clear / history) which must NOT
///     touch the repository
///   - real command execution through the repository, with stdout/stderr
///     formatting and exit-code markers
///   - the explicit-text overload ExecuteAsync(string) which is what the
///     Enter key handler uses (so a slow @bind round-trip cannot cause
///     Enter to execute a stale / empty command)
///   - history buffer behaviour (recording, de-duplication, Up/Down
///     navigation, edge cases)
///   - the re-entrancy / busy-state race guard that used to make the
///     terminal appear "frozen"
///   - cancellation and dispose semantics
/// </summary>
public sealed class TerminalViewModelTests
{
    private sealed class FakeTerminalRepository : ITerminalRepository
    {
        public string? LastCommand { get; private set; }
        public CancellationToken LastToken { get; private set; }
        public int CallCount { get; private set; }

        public Func<string, CancellationToken, CliResult>? Handler { get; set; }

        public Task<CliResult> ExecuteAsync(string commandLine, CancellationToken ct = default)
        {
            LastCommand = commandLine;
            LastToken = ct;
            CallCount++;
            var r = Handler?.Invoke(commandLine, ct)
                    ?? new CliResult { ExitCode = 0, Stdout = $"ok:{commandLine}", Stderr = "" };
            return Task.FromResult(r);
        }
    }

    private static TerminalViewModel NewVm(FakeTerminalRepository repo) =>
        new(repo, NullLogger<TerminalViewModel>.Instance);

    // ---- Initial state ----

    [Fact]
    public void Constructor_SeedBannerSoPageIsNotEmpty()
    {
        var repo = new FakeTerminalRepository();
        var vm = NewVm(repo);

        Assert.NotEmpty(vm.Lines);
        Assert.Contains(vm.Lines, l => l.Text.Contains("help"));
        Assert.Contains(vm.Lines, l => l.Text.Contains("clear"));
        Assert.False(vm.IsBusy);
        Assert.Equal("", vm.Input);
        Assert.False(vm.HasHistory);
        Assert.Equal(0, repo.CallCount);   // no cscli call on startup
    }

    // ---- Empty input ----

    [Fact]
    public async Task ExecuteAsync_EmptyInput_NoOpAndDoesNotCallRepo()
    {
        var repo = new FakeTerminalRepository();
        var vm = NewVm(repo);

        await vm.ExecuteAsync();

        Assert.Equal(0, repo.CallCount);
        Assert.False(vm.IsBusy);
        Assert.NotEmpty(vm.Lines);
    }

    [Fact]
    public async Task ExecuteAsync_WhitespaceOnly_NoOpAndDoesNotCallRepo()
    {
        var repo = new FakeTerminalRepository();
        var vm = NewVm(repo);
        var initialCount = vm.Lines.Count;

        await vm.ExecuteAsync("    \t   ");

        Assert.Equal(0, repo.CallCount);
        Assert.Equal(initialCount, vm.Lines.Count);
    }

    [Fact]
    public async Task ExecuteAsync_ClearsInputAfterSubmit()
    {
        var repo = new FakeTerminalRepository();
        var vm = NewVm(repo);

        await vm.ExecuteAsync("decisions list");

        Assert.Equal("", vm.Input);
    }

    // ---- Explicit-text overload (used by the Enter key handler) ----

    [Fact]
    public async Task ExecuteAsync_WithStringOverload_RunsThatTextRegardlessOfVmInput()
    {
        // This is the regression test for "Enter does nothing": the page
        // captures the DOM value on every keystroke and passes it explicitly
        // to ExecuteAsync(string), so a slow @bind round-trip cannot leave
        // Vm.Input empty when Enter fires.
        var repo = new FakeTerminalRepository();
        var vm = NewVm(repo);
        vm.Input = "stale value from a previous render";

        await vm.ExecuteAsync("decisions list");

        // The explicit text wins, not the stale Vm.Input.
        Assert.Equal("decisions list", repo.LastCommand);
        Assert.Equal(1, repo.CallCount);
        Assert.Contains(vm.Lines, l => l.Class == "cmd" && l.Text == "decisions list");
        Assert.Contains(vm.Lines, l => l.Class == "out" && l.Text == "ok:decisions list");
    }

    [Fact]
    public async Task ExecuteAsync_ParameterlessOverload_UsesCurrentInput()
    {
        var repo = new FakeTerminalRepository();
        var vm = NewVm(repo);
        vm.Input = "alerts list";

        await vm.ExecuteAsync();

        Assert.Equal("alerts list", repo.LastCommand);
    }

    [Fact]
    public async Task ExecuteAsync_StringOverloadClearsInputEvenOnBuiltInCommand()
    {
        var repo = new FakeTerminalRepository();
        var vm = NewVm(repo);
        vm.Input = "garbage that should be cleared";

        await vm.ExecuteAsync("help");

        Assert.Equal("", vm.Input);
        Assert.Equal(0, repo.CallCount);   // help is local
    }

    // ---- Built-in commands (no repo call) ----

    [Fact]
    public async Task ExecuteAsync_HelpCommand_PrintsHelpLocallyWithoutCallingRepo()
    {
        var repo = new FakeTerminalRepository();
        var vm = NewVm(repo);

        await vm.ExecuteAsync("help");

        Assert.Equal(0, repo.CallCount);   // never reaches cscli
        Assert.Contains(vm.Lines, l => l.Class == "out" && l.Text.Contains("decisions list"));
        Assert.Contains(vm.Lines, l => l.Class == "cmd" && l.Text == "help");
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task ExecuteAsync_HelpIsCaseInsensitive()
    {
        var repo = new FakeTerminalRepository();
        var vm = NewVm(repo);

        await vm.ExecuteAsync("HELP");

        Assert.Equal(0, repo.CallCount);
        Assert.Contains(vm.Lines, l => l.Class == "out" && l.Text.Contains("decisions list"));
    }

    [Fact]
    public async Task ExecuteAsync_ClearCommand_WipesLinesAndLeavesMarker()
    {
        var repo = new FakeTerminalRepository();
        var vm = NewVm(repo);
        // Seed some noise first so clear has something to wipe.
        await vm.ExecuteAsync("help");
        Assert.True(vm.Lines.Count > 2);

        await vm.ExecuteAsync("clear");

        Assert.Equal(0, repo.CallCount);
        Assert.Single(vm.Lines);
        Assert.Equal("(screen cleared)", vm.Lines[0].Text);
        Assert.Equal("muted", vm.Lines[0].Class);
    }

    [Fact]
    public async Task ExecuteAsync_HistoryCommand_WhenEmpty_PrintsEmptyMarker()
    {
        var repo = new FakeTerminalRepository();
        var vm = NewVm(repo);

        await vm.ExecuteAsync("history");

        Assert.Equal(0, repo.CallCount);
        Assert.Contains(vm.Lines, l => l.Class == "muted" && l.Text.Contains("history is empty"));
    }

    [Fact]
    public async Task ExecuteAsync_HistoryCommand_WhenNonEmpty_PrintsNumberedHistory()
    {
        var repo = new FakeTerminalRepository();
        var vm = NewVm(repo);

        await vm.ExecuteAsync("decisions list");
        await vm.ExecuteAsync("alerts list");
        Assert.Equal(2, repo.CallCount);

        await vm.ExecuteAsync("history");

        // The history command is local and must NOT reach cscli.
        Assert.Equal(2, repo.CallCount);

        var historyLine = vm.Lines.FirstOrDefault(l => l.Text.Contains("decisions list") && l.Text.Contains("alerts list"));
        Assert.NotNull(historyLine);
        Assert.Contains("  1", historyLine!.Text);
        Assert.Contains("  2", historyLine.Text);
    }

    // ---- Real commands (repo is called) ----

    [Fact]
    public async Task ExecuteAsync_RealCommand_CallsRepoWithRawInputAndEchoesPrompt()
    {
        var repo = new FakeTerminalRepository();
        var vm = NewVm(repo);

        await vm.ExecuteAsync("decisions list");

        Assert.Equal(1, repo.CallCount);
        Assert.Equal("decisions list", repo.LastCommand);
        Assert.Contains(vm.Lines, l => l.Class == "cmd" && l.IsPrompt && l.Text == "decisions list");
        Assert.Contains(vm.Lines, l => l.Class == "out" && l.Text == "ok:decisions list");
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task ExecuteAsync_SuccessWithStdout_PrintsOutLines()
    {
        var repo = new FakeTerminalRepository
        {
            Handler = (_, _) => new CliResult { ExitCode = 0, Stdout = "line1\nline2\nline3", Stderr = "" }
        };
        var vm = NewVm(repo);

        await vm.ExecuteAsync("decisions list");

        Assert.Contains(vm.Lines, l => l.Class == "out" && l.Text == "line1");
        Assert.Contains(vm.Lines, l => l.Class == "out" && l.Text == "line2");
        Assert.Contains(vm.Lines, l => l.Class == "out" && l.Text == "line3");
    }

    [Fact]
    public async Task ExecuteAsync_NonZeroExitWithStderr_PrintsAllOutputAsErrWithExitCode()
    {
        // When the cscli exit code is non-zero, ALL output lines (stdout
        // + stderr concatenated) are flagged as "err" so the user can see
        // at a glance that the command failed. The exit code is appended
        // as a final marker line.
        var repo = new FakeTerminalRepository
        {
            Handler = (_, _) => new CliResult { ExitCode = 1, Stdout = "partial output", Stderr = "boom: bad flag" }
        };
        var vm = NewVm(repo);

        await vm.ExecuteAsync("decisions add --bad");

        Assert.Contains(vm.Lines, l => l.Class == "err" && l.Text == "partial output");
        Assert.Contains(vm.Lines, l => l.Class == "err" && l.Text == "boom: bad flag");
        Assert.Contains(vm.Lines, l => l.Class == "err" && l.Text.Contains("exit code 1"));
    }

    [Fact]
    public async Task ExecuteAsync_NoOutput_PrintsExitCodePlaceholder()
    {
        var repo = new FakeTerminalRepository
        {
            Handler = (_, _) => new CliResult { ExitCode = 0, Stdout = "", Stderr = "" }
        };
        var vm = NewVm(repo);

        await vm.ExecuteAsync("metrics");

        Assert.Contains(vm.Lines, l => l.Class == "out" && l.Text.Contains("no output") && l.Text.Contains("exit code 0"));
    }

    [Fact]
    public async Task ExecuteAsync_RepoThrows_ExceptionMessageIsRenderedAsErrLine()
    {
        var repo = new FakeTerminalRepository
        {
            Handler = (_, _) => throw new InvalidOperationException("boom")
        };
        var vm = NewVm(repo);

        await vm.ExecuteAsync("decisions list");

        Assert.Contains(vm.Lines, l => l.Class == "err" && l.Text.Contains("[error]") && l.Text.Contains("boom"));
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task ExecuteAsync_Cancellation_ProducesTimeoutLine()
    {
        var repo = new FakeTerminalRepository
        {
            Handler = (_, ct) => throw new OperationCanceledException(ct)
        };
        var vm = NewVm(repo);

        await vm.ExecuteAsync("long-running");

        Assert.Contains(vm.Lines, l => l.Class == "err" && l.Text.Contains("timed out"));
        Assert.False(vm.IsBusy);
    }

    // ---- History navigation ----

    [Fact]
    public void BrowseHistory_EmptyHistory_IsNoOp()
    {
        var vm = NewVm(new FakeTerminalRepository());
        vm.Input = "anything";
        vm.BrowseHistory(-1);
        Assert.Equal("anything", vm.Input);
        vm.BrowseHistory(1);
        Assert.Equal("anything", vm.Input);
    }

    [Fact]
    public async Task BrowseHistory_ArrowUp_RecallsLastCommand()
    {
        var vm = NewVm(new FakeTerminalRepository());
        await vm.ExecuteAsync("decisions list");
        await vm.ExecuteAsync("alerts list");

        vm.BrowseHistory(-1);   // ArrowUp -> most recent first
        Assert.Equal("alerts list", vm.Input);

        vm.BrowseHistory(-1);   // ArrowUp again -> older
        Assert.Equal("decisions list", vm.Input);
    }

    [Fact]
    public async Task BrowseHistory_ArrowDownFromStart_IsNoOp()
    {
        // After ExecuteAsync the input buffer is cleared. ArrowDown without
        // a prior ArrowUp is a no-op, so the input stays empty.
        var vm = NewVm(new FakeTerminalRepository());
        await vm.ExecuteAsync("decisions list");
        Assert.Equal("", vm.Input);

        vm.BrowseHistory(1);
        Assert.Equal("", vm.Input);
    }

    [Fact]
    public async Task BrowseHistory_ArrowDownAfterArrowUp_GoesForward()
    {
        var vm = NewVm(new FakeTerminalRepository());
        await vm.ExecuteAsync("decisions list");
        await vm.ExecuteAsync("alerts list");

        vm.BrowseHistory(-1);   // -> alerts list
        vm.BrowseHistory(-1);   // -> decisions list
        vm.BrowseHistory(1);    // -> alerts list
        Assert.Equal("alerts list", vm.Input);
    }

    [Fact]
    public async Task BrowseHistory_ArrowDownPastNewest_ClearsInput()
    {
        var vm = NewVm(new FakeTerminalRepository());
        await vm.ExecuteAsync("decisions list");

        vm.BrowseHistory(-1);   // -> decisions list
        vm.BrowseHistory(1);    // past newest -> clear
        Assert.Equal("", vm.Input);
    }

    [Fact]
    public async Task BrowseHistory_RoundTripPreservesOrder()
    {
        var vm = NewVm(new FakeTerminalRepository());
        foreach (var cmd in new[] { "a", "b", "c" })
            await vm.ExecuteAsync(cmd);

        // Backwards through history: c, b, a
        vm.BrowseHistory(-1); Assert.Equal("c", vm.Input);
        vm.BrowseHistory(-1); Assert.Equal("b", vm.Input);
        vm.BrowseHistory(-1); Assert.Equal("a", vm.Input);

        // Forwards: b, c, then empty past newest.
        vm.BrowseHistory(1); Assert.Equal("b", vm.Input);
        vm.BrowseHistory(1); Assert.Equal("c", vm.Input);
        vm.BrowseHistory(1); Assert.Equal("", vm.Input);
    }

    [Fact]
    public async Task ExecuteAsync_DoesNotRecordDuplicateConsecutiveCommand()
    {
        var vm = NewVm(new FakeTerminalRepository());
        await vm.ExecuteAsync("decisions list");
        await vm.ExecuteAsync("decisions list");

        // Two identical consecutive commands -> only one history entry.
        vm.BrowseHistory(-1);   // -> decisions list (cursor at 0)
        Assert.Equal("decisions list", vm.Input);
        // Another ArrowUp tries to go older. With a single entry, the
        // cursor wraps to -1 and clears the input.
        vm.BrowseHistory(-1);
        Assert.Equal("", vm.Input);
        Assert.True(vm.HasHistory);
    }

    // ---- Busy state + race guard ----

    [Fact]
    public async Task ExecuteAsync_SetsBusyDuringCallAndClearsAfter()
    {
        var tcs = new TaskCompletionSource<CliResult>();
        var repo = new FakeTerminalRepository
        {
            Handler = (_, _) => tcs.Task.Result   // blocks until tcs.SetResult
        };
        var vm = NewVm(repo);

        var task = vm.ExecuteAsync("decisions list");
        await Task.Yield();
        await Task.Delay(20);

        Assert.True(vm.IsBusy);

        tcs.SetResult(new CliResult { ExitCode = 0, Stdout = "done", Stderr = "" });
        await task;

        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task ExecuteAsync_ConcurrentSecondCallWaitsForFirstToSettle()
    {
        // Regression test for "terminal appears frozen": the old finally
        // block would set _busy=false AFTER the new command had already
        // set it to true. The VM now has a _pendingCount re-entrancy guard.
        var firstTcs = new TaskCompletionSource<CliResult>();
        var repo = new FakeTerminalRepository();
        var vm = NewVm(repo);

        repo.Handler = (_, _) => firstTcs.Task.Result;
        var firstTask = vm.ExecuteAsync("cmd-a");
        await Task.Yield();
        await Task.Delay(20);
        Assert.True(vm.IsBusy);

        repo.Handler = (_, _) => new CliResult { ExitCode = 0, Stdout = "ok-b", Stderr = "" };
        var secondTask = vm.ExecuteAsync("cmd-b");

        firstTcs.SetResult(new CliResult { ExitCode = 0, Stdout = "ok-a", Stderr = "" });

        await firstTask;
        await secondTask;

        Assert.False(vm.IsBusy);
        Assert.Equal(2, repo.CallCount);
        Assert.Contains(vm.Lines, l => l.Text == "ok-a");
        Assert.Contains(vm.Lines, l => l.Text == "ok-b");
    }

    // ---- ShowHelp / Clear button handlers ----

    [Fact]
    public void ShowHelp_AddsHelpLinesWithoutCallingRepo()
    {
        var repo = new FakeTerminalRepository();
        var vm = NewVm(repo);
        var before = vm.Lines.Count;

        vm.ShowHelp();

        Assert.Equal(0, repo.CallCount);
        Assert.True(vm.Lines.Count > before);
        Assert.Contains(vm.Lines, l => l.Class == "out" && l.Text.Contains("Common cscli commands"));
    }

    [Fact]
    public void Clear_WipesLinesAndLeavesMarker()
    {
        var vm = NewVm(new FakeTerminalRepository());
        vm.ShowHelp();
        Assert.True(vm.Lines.Count > 1);

        vm.Clear();

        Assert.Single(vm.Lines);
        Assert.Equal("(screen cleared)", vm.Lines[0].Text);
    }

    // ---- Cancel / Dispose ----

    [Fact]
    public void Cancel_WithNoCommand_IsSafeNoOp()
    {
        var vm = NewVm(new FakeTerminalRepository());
        vm.Cancel();   // must not throw even with no CTS allocated
    }

    [Fact]
    public void Dispose_CancelsAndReleasesCts()
    {
        var vm = NewVm(new FakeTerminalRepository());
        vm.Dispose();
        vm.Dispose();   // idempotent
    }

    // ---- INotifyPropertyChanged ----

    [Fact]
    public void Input_PropertyChange_RaisesINotifyPropertyChanged()
    {
        var vm = NewVm(new FakeTerminalRepository());
        var changes = new List<string?>();
        ((INotifyPropertyChanged)vm).PropertyChanged += (_, e) => changes.Add(e.PropertyName);

        vm.Input = "decisions";
        vm.Input = "decisions list";

        Assert.Contains(nameof(TerminalViewModel.Input), changes);
    }

    [Fact]
    public async Task ExecuteAsync_BusyStateChange_RaisesINotifyPropertyChanged()
    {
        var tcs = new TaskCompletionSource<CliResult>();
        var repo = new FakeTerminalRepository { Handler = (_, _) => tcs.Task.Result };
        var vm = NewVm(repo);
        var changes = new List<string?>();
        ((INotifyPropertyChanged)vm).PropertyChanged += (_, e) => changes.Add(e.PropertyName);

        var task = vm.ExecuteAsync("decisions list");
        await Task.Yield();
        await Task.Delay(20);

        tcs.SetResult(new CliResult { ExitCode = 0, Stdout = "ok", Stderr = "" });
        await task;

        var busyChanges = changes.Where(n => n == nameof(TerminalViewModel.IsBusy)).ToList();
        Assert.True(busyChanges.Count >= 2, $"expected >= 2 IsBusy changes, got {busyChanges.Count}");
    }

    [Fact]
    public async Task ExecuteAsync_LinesPropertyChange_RaisedWhenNewLineAdded()
    {
        var vm = NewVm(new FakeTerminalRepository());
        var changes = new List<string?>();
        ((INotifyPropertyChanged)vm).PropertyChanged += (_, e) => changes.Add(e.PropertyName);

        await vm.ExecuteAsync("decisions list");

        var linesChanges = changes.Where(n => n == nameof(TerminalViewModel.Lines)).ToList();
        Assert.True(linesChanges.Count >= 2, $"expected >= 2 Lines changes, got {linesChanges.Count}");
    }
}
