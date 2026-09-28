namespace WebMacros.Engine.Syntax;

/// <summary>A line could not be parsed. <see cref="LineNumber"/> is 1-based (0 if unknown).</summary>
public sealed class MacroSyntaxException : Exception
{
    public int LineNumber { get; }
    public string Reason { get; }

    public MacroSyntaxException(string message, int lineNumber = 0)
        : base(lineNumber > 0 ? $"Line {lineNumber}: {message}" : message)
    {
        LineNumber = lineNumber;
        Reason = message;
    }

    public MacroSyntaxException WithLine(int line) => new(Reason, line);
}
