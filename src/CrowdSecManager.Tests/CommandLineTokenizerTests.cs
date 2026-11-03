using CrowdSecManager.Core.CommandLine;
using Xunit;

namespace CrowdSecManager.Tests;

/// <summary>
/// Unit tests for <see cref="CommandLineTokenizer"/> - the POSIX-shell-like
/// tokenizer that splits the user's free-form command line into argv tokens
/// for the cscli process. This is the security-critical boundary that
/// prevents shell injection: the tokens are passed directly to
/// <c>ProcessStartInfo.ArgumentList</c>, never through a shell.
/// </summary>
public sealed class CommandLineTokenizerTests
{
    // ---- Basic tokenization ----

    [Fact]
    public void Tokenize_NullOrEmpty_ReturnsEmptyList()
    {
        Assert.Empty(CommandLineTokenizer.Tokenize(null));
        Assert.Empty(CommandLineTokenizer.Tokenize(""));
        Assert.Empty(CommandLineTokenizer.Tokenize("   "));
        Assert.Empty(CommandLineTokenizer.Tokenize("\t\n"));
    }

    [Fact]
    public void Tokenize_SingleWord_ReturnsOneToken()
    {
        var tokens = CommandLineTokenizer.Tokenize("decisions");
        Assert.Equal(new[] { "decisions" }, tokens);
    }

    [Fact]
    public void Tokenize_MultipleWords_SplitsOnWhitespace()
    {
        var tokens = CommandLineTokenizer.Tokenize("decisions list -o json");
        Assert.Equal(new[] { "decisions", "list", "-o", "json" }, tokens);
    }

    [Fact]
    public void Tokenize_CollapsesRepeatedWhitespace()
    {
        var tokens = CommandLineTokenizer.Tokenize("decisions    list\t\t-o   json");
        Assert.Equal(new[] { "decisions", "list", "-o", "json" }, tokens);
    }

    [Fact]
    public void Tokenize_TrimsLeadingAndTrailingWhitespace()
    {
        var tokens = CommandLineTokenizer.Tokenize("   decisions list   ");
        Assert.Equal(new[] { "decisions", "list" }, tokens);
    }

    // ---- Quoted strings ----

    [Fact]
    public void Tokenize_DoubleQuotedString_KeepsInnerWhitespaceAndDropsQuotes()
    {
        var tokens = CommandLineTokenizer.Tokenize("decisions add --reason \"manual review\"");
        Assert.Equal(new[] { "decisions", "add", "--reason", "manual review" }, tokens);
    }

    [Fact]
    public void Tokenize_EmptyDoubleQuotes_DoesNotProduceEmptyToken()
    {
        // Our tokenizer deliberately does NOT emit an empty token for ""
        // (unlike POSIX shells). Empty argv tokens are rarely useful for
        // cscli and almost always a user mistake, so we drop them.
        var tokens = CommandLineTokenizer.Tokenize("decisions \"\" list");
        Assert.Equal(new[] { "decisions", "list" }, tokens);
    }

    [Fact]
    public void Tokenize_QuotedStringAdjacentToWord_Concatenates()
    {
        var tokens = CommandLineTokenizer.Tokenize("foo\"bar baz\"qux");
        Assert.Equal(new[] { "foobar bazqux" }, tokens);
    }

    [Fact]
    public void Tokenize_QuotedStringInsideWord_KeepsInnerSpaces()
    {
        var tokens = CommandLineTokenizer.Tokenize("--reason=\"some reason here");
        Assert.Equal(new[] { "--reason=some reason here" }, tokens);
    }

    [Fact]
    public void Tokenize_UnclosedQuote_KeepsEverythingToEndOfLine()
    {
        var tokens = CommandLineTokenizer.Tokenize("decisions --reason \"unfinished");
        Assert.Equal(new[] { "decisions", "--reason", "unfinished" }, tokens);
    }

    // ---- Backslash escaping ----

    [Fact]
    public void Tokenize_BackslashEscapesNextCharacter()
    {
        var tokens = CommandLineTokenizer.Tokenize("decisions\\ list");
        Assert.Equal(new[] { "decisions list" }, tokens);
    }

    [Fact]
    public void Tokenize_BackslashEscapesQuoteInsideUnquotedWord()
    {
        var tokens = CommandLineTokenizer.Tokenize("decisions \\\"list\\\"");
        Assert.Equal(new[] { "decisions", "\"list\"" }, tokens);
    }

    [Fact]
    public void Tokenize_BackslashAtEndOfLine_IsKeptAsLiteralBackslash()
    {
        var tokens = CommandLineTokenizer.Tokenize("decisions\\");
        Assert.Equal(new[] { "decisions\\" }, tokens);
    }

    [Fact]
    public void Tokenize_BackslashNIsNotInterpretedAsNewline()
    {
        // The tokenizer treats backslash as "escape the next character"
        // (POSIX-style). So \n becomes a literal 'n' (the backslash is
        // consumed), but it does NOT become an actual newline character.
        var tokens = CommandLineTokenizer.Tokenize("hello\\nworld");
        Assert.Equal(new[] { "hellonworld" }, tokens);
    }

    // ---- Security / shell-injection resistance ----

    [Theory]
    [InlineData("decisions list; rm -rf /", new[] { "decisions", "list;", "rm", "-rf", "/" })]
    [InlineData("decisions list | grep x", new[] { "decisions", "list", "|", "grep", "x" })]
    [InlineData("decisions list &", new[] { "decisions", "list", "&" })]
    [InlineData("decisions list > /etc/passwd", new[] { "decisions", "list", ">", "/etc/passwd" })]
    [InlineData("decisions list `whoami`", new[] { "decisions", "list", "`whoami`" })]
    [InlineData("decisions list $(whoami)", new[] { "decisions", "list", "$(whoami)" })]
    [InlineData("decisions list ${HOME}", new[] { "decisions", "list", "${HOME}" })]
    [InlineData("decisions list ~", new[] { "decisions", "list", "~" })]
    public void Tokenize_DoesNotInterpretShellMetacharacters(string input, string[] expected)
    {
        // The metacharacters are kept as literal characters in the tokens.
        // They are NOT interpreted as shell operators because there is no
        // shell - the tokens go straight to cscli via ArgumentList.
        var tokens = CommandLineTokenizer.Tokenize(input);
        Assert.Equal(expected, tokens);
    }

    [Fact]
    public void Tokenize_CommandSubstitutionStaysLiteral()
    {
        var tokens = CommandLineTokenizer.Tokenize("alerts $(malicious)");
        Assert.Equal(new[] { "alerts", "$(malicious)" }, tokens);
    }

    // ---- TokenizeAndStripCscli ----

    [Fact]
    public void TokenizeAndStripCscli_StripsLeadingCscliTokenCaseInsensitively()
    {
        Assert.Equal(new[] { "decisions", "list" },
            CommandLineTokenizer.TokenizeAndStripCscli("cscli decisions list"));
        Assert.Equal(new[] { "decisions", "list" },
            CommandLineTokenizer.TokenizeAndStripCscli("CSCLI decisions list"));
        Assert.Equal(new[] { "decisions", "list" },
            CommandLineTokenizer.TokenizeAndStripCscli("Cscli decisions list"));
    }

    [Fact]
    public void TokenizeAndStripCscli_KeepsArgsWhenNoLeadingCscli()
    {
        Assert.Equal(new[] { "decisions", "list" },
            CommandLineTokenizer.TokenizeAndStripCscli("decisions list"));
    }

    [Fact]
    public void TokenizeAndStripCscli_DoesNotStripCscliWhenNotFirstToken()
    {
        // Only the FIRST token is checked for "cscli"; a cscli deeper in
        // the command line (e.g. as an argument value) is preserved.
        Assert.Equal(new[] { "--reason", "ran cscli by mistake" },
            CommandLineTokenizer.TokenizeAndStripCscli("--reason \"ran cscli by mistake\""));
    }

    [Fact]
    public void TokenizeAndStripCscli_EmptyInput_ReturnsEmptyList()
    {
        Assert.Empty(CommandLineTokenizer.TokenizeAndStripCscli(null));
        Assert.Empty(CommandLineTokenizer.TokenizeAndStripCscli(""));
        Assert.Empty(CommandLineTokenizer.TokenizeAndStripCscli("   "));
    }

    [Fact]
    public void TokenizeAndStripCscli_OnlyCscli_ReturnsEmpty()
    {
        // "cscli" alone becomes nothing after stripping - the CLI service
        // treats this as "no command entered" and returns a friendly hint.
        Assert.Empty(CommandLineTokenizer.TokenizeAndStripCscli("cscli"));
        Assert.Empty(CommandLineTokenizer.TokenizeAndStripCscli("   cscli   "));
    }

    // ---- FirstTokenLower ----

    [Fact]
    public void FirstTokenLower_ReturnsLowercasedFirstToken()
    {
        Assert.Equal("decisions", CommandLineTokenizer.FirstTokenLower("decisions list"));
        Assert.Equal("decisions", CommandLineTokenizer.FirstTokenLower("DECISIONS list"));
        Assert.Equal("decisions", CommandLineTokenizer.FirstTokenLower("DeCiSiOnS list"));
    }

    [Fact]
    public void FirstTokenLower_ReturnsFirstQuotedTokenLowered()
    {
        Assert.Equal("decisions", CommandLineTokenizer.FirstTokenLower("\"decisions\" list"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void FirstTokenLower_EmptyInput_ReturnsEmptyString(string? input)
        => Assert.Equal("", CommandLineTokenizer.FirstTokenLower(input));

    [Theory]
    [InlineData("HELP", "help")]
    [InlineData("Help", "help")]
    [InlineData("help", "help")]
    [InlineData("CLEAR", "clear")]
    [InlineData("History", "history")]
    public void FirstTokenLower_SupportsCaseInsensitiveBuiltInDetection(string input, string expected)
        => Assert.Equal(expected, CommandLineTokenizer.FirstTokenLower(input));
}
