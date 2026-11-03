using System.Text;

namespace CrowdSecManager.Core.CommandLine;

/// <summary>
/// Tokenize a free-form command line into argv tokens, the way a POSIX
/// shell would tokenize a simple command line (without any shell expansion,
/// redirection or piping). Respects double quotes (which are removed) and
/// backslash escaping of the next character. Whitespace outside quotes
/// separates tokens.
///
/// This is the contract used by the interactive terminal: the user types
/// text, it is tokenized here, and the tokens are passed directly to cscli
/// via <c>ProcessStartInfo.ArgumentList</c> (NO shell), so metacharacters
/// such as <c>;</c>, <c>|</c> or <c>&amp;</c> cannot break out of cscli.
///
/// Shared by <see cref="CrowdSecCliService.RunTerminalAsync"/> (Infrastructure)
/// and <c>TerminalViewModel</c> (ViewModels) so the behavior stays consistent
/// across the CLI service and the terminal page, and so it can be covered by
/// unit tests (<c>CommandLineTokenizerTests</c>).
/// </summary>
public static class CommandLineTokenizer
{
    /// <summary>
    /// Split a command line into argv tokens. Returns an empty list for
    /// null / empty / whitespace-only input.
    /// </summary>
    public static IReadOnlyList<string> Tokenize(string? line)
    {
        var tokens = new List<string>();
        if (string.IsNullOrWhiteSpace(line)) return tokens;

        var sb = new StringBuilder();
        bool inQuotes = false;
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (c == '\\' && i + 1 < line.Length) { sb.Append(line[++i]); continue; }
            if (c == '"') { inQuotes = !inQuotes; continue; }
            if (char.IsWhiteSpace(c) && !inQuotes)
            {
                if (sb.Length > 0) { tokens.Add(sb.ToString()); sb.Clear(); }
            }
            else sb.Append(c);
        }
        if (sb.Length > 0) tokens.Add(sb.ToString());
        return tokens;
    }

    /// <summary>
    /// Same as <see cref="Tokenize"/> but also drops a leading literal
    /// <c>cscli</c> token when the user typed it explicitly. This is what
    /// <see cref="CrowdSecCliService.RunTerminalAsync"/> uses before
    /// forwarding argv to the cscli process.
    /// </summary>
    public static IReadOnlyList<string> TokenizeAndStripCscli(string? line)
    {
        var tokens = Tokenize(line);
        if (tokens.Count > 0 && string.Equals(tokens[0], "cscli", StringComparison.OrdinalIgnoreCase))
            return tokens.Skip(1).ToList();
        return tokens;
    }

    /// <summary>
    /// First token of the line, lowercased for case-insensitive comparison.
    /// Returns the empty string for empty input. Used by the terminal
    /// ViewModel to detect local built-in commands (help / clear / history).
    /// </summary>
    public static string FirstTokenLower(string? line)
    {
        foreach (var t in Tokenize(line))
            return t.ToLowerInvariant();
        return "";
    }
}
