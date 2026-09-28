namespace WebMacros.Engine.Syntax;

/// <summary>
/// One whitespace-separated parameter of a macro line, e.g. <c>POS=1</c>, <c>CONTENT="a b"</c> or <c>!VAR1</c>.
/// </summary>
/// <param name="Raw">The token exactly as written (quotes included).</param>
/// <param name="Key">Upper-cased key for <c>KEY=value</c> tokens; null for positional tokens.</param>
/// <param name="Value">The value with surrounding quotes removed and backslash escapes resolved
/// (only when the whole value was quoted). Variables and &lt;SP&gt;-style escapes are NOT resolved yet.</param>
/// <param name="Quoted">True when the value was written as a quoted string.</param>
public sealed record Token(string Raw, string? Key, string Value, bool Quoted)
{
    public bool IsPositional => Key is null;
    public override string ToString() => Raw;
}
