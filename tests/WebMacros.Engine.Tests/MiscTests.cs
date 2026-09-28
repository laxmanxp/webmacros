using WebMacros.Engine.Runtime;
using WebMacros.Engine.Samples;
using WebMacros.Engine.Syntax;

namespace WebMacros.Engine.Tests;

public class MiscTests
{
    [Fact]
    public void CsvParsesQuotesNewlinesAndDelimiters()
    {
        var rows = Csv.Parse("a,\"b,c\",\"say \"\"hi\"\"\"\r\n\"multi\nline\",x\n\n");
        Assert.Equal(2, rows.Count);
        Assert.Equal(new[] { "a", "b,c", "say \"hi\"" }, rows[0]);
        Assert.Equal(new[] { "multi\nline", "x" }, rows[1]);
        Assert.Equal(new[] { "1", "2" }, Csv.Parse("1;2", ';')[0]);
        Assert.Equal(new[] { "", "" }, Csv.Parse(",\n")[0]);
        Assert.Throws<FormatException>(() => Csv.Parse("\"open"));
    }

    [Fact]
    public void CsvFormatQuotesEverything() =>
        Assert.Equal("\"a\",\"b \"\"q\"\"\",\"\"", Csv.FormatRow(new[] { "a", "b \"q\"", "" }));

    [Theory]
    [InlineData("yyyy-mm-dd hh:nn:ss", "2026-01-05 07:08:09")]
    [InlineData("yymmdd", "260105")]
    [InlineData("doy dow", "005 2")]
    [InlineData("literal!", "literal!")]
    public void NowFormatter_Formats(string format, string expected) =>
        Assert.Equal(expected, NowFormatter.Format(new DateTimeOffset(2026, 1, 5, 7, 8, 9, TimeSpan.Zero), format));

    [Fact]
    public void EscapesAreResolved() => Assert.Equal("a b\nc\td", MacroState.Unescape("a<SP>b<br>c<TAB>d"));

    [Fact]
    public void SampleMacrosAllParse()
    {
        Assert.Equal(4, SampleMacros.Macros.Count);
        foreach (var s in SampleMacros.Macros)
        {
            Assert.EndsWith(".iim", s.FileName);
            Assert.Empty(MacroParser.Validate(s.Content));
            Assert.NotEmpty(MacroParser.Parse(s.Content).Commands);
        }
        var rows = Csv.Parse(SampleMacros.CustomersCsv.Content);
        Assert.Equal(4, rows.Count);
        Assert.All(rows, r => Assert.Equal(7, r.Count));
    }

    [Fact]
    public void SupportedCommandListMatchesParser()
    {
        foreach (var c in MacroParser.SupportedCommands)
            Assert.DoesNotContain(c, MacroParser.UnsupportedCommands);
    }
}
