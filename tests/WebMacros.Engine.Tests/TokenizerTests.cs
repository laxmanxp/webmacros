using WebMacros.Engine.Syntax;

namespace WebMacros.Engine.Tests;

public class TokenizerTests
{
    [Fact]
    public void SplitsCommandAndKeyValueTokens()
    {
        var (cmd, tokens) = Tokenizer.TokenizeLine("tag POS=1 TYPE=INPUT:TEXT ATTR=NAME:q CONTENT=hello<SP>world");
        Assert.Equal("TAG", cmd);
        Assert.Equal(4, tokens.Count);
        Assert.Equal("POS", tokens[0].Key);
        Assert.Equal("INPUT:TEXT", tokens[1].Value);
        Assert.Equal("NAME:q", tokens[2].Value);
        Assert.Equal("hello<SP>world", tokens[3].Value);
    }

    [Fact]
    public void QuotedValuesKeepSpacesAndUnescape()
    {
        var (_, tokens) = Tokenizer.TokenizeLine("TAG XPATH=\"//a[@title=\\\"x y\\\"]\" CONTENT=\"a b\\\\c\"");
        Assert.Equal("//a[@title=\"x y\"]", tokens[0].Value);
        Assert.True(tokens[0].Quoted);
        Assert.Equal("a b\\c", tokens[1].Value);
    }

    [Fact]
    public void KeyIsUppercasedAndOnlyFirstEqualsSplits()
    {
        var (_, tokens) = Tokenizer.TokenizeLine("URL goto=https://x.test/?a=b&c=d");
        Assert.Equal("GOTO", tokens[0].Key);
        Assert.Equal("https://x.test/?a=b&c=d", tokens[0].Value);
    }

    [Fact]
    public void PositionalTokensHaveNoKey()
    {
        var (_, tokens) = Tokenizer.TokenizeLine("SET !VAR1 hello");
        Assert.True(tokens[0].IsPositional);
        Assert.Equal("!VAR1", tokens[0].Value);
        Assert.Equal("hello", tokens[1].Value);
    }

    [Fact]
    public void EvalWithQuotedSpacesIsOneToken()
    {
        var (_, tokens) = Tokenizer.TokenizeLine("SET !VAR1 EVAL(\"var a = 'x y'; a;\")");
        Assert.Equal(2, tokens.Count);
        Assert.Equal("EVAL(\"var a = 'x y'; a;\")", tokens[1].Raw);
        Assert.False(tokens[1].Quoted);
    }

    [Fact]
    public void UnterminatedQuoteThrows()
    {
        Assert.Throws<MacroSyntaxException>(() => Tokenizer.TokenizeLine("TAG CONTENT=\"abc"));
    }

    [Theory]
    [InlineData("abc", "abc")]
    [InlineData("a b", "\"a b\"")]
    [InlineData("say \"hi\"", "\"say \\\"hi\\\"\"")]
    public void QuoteIfNeeded(string input, string expected) => Assert.Equal(expected, Tokenizer.QuoteIfNeeded(input));

    [Fact]
    public void QuoteRoundTrips()
    {
        var v = "a \"b\" \\ c";
        var (_, tokens) = Tokenizer.TokenizeLine("SET x " + Tokenizer.QuoteIfNeeded(v));
        Assert.Equal(v, tokens[1].Value);
    }
}
