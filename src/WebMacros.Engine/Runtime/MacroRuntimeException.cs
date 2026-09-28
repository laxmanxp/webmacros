namespace WebMacros.Engine.Runtime;

/// <summary>A command failed while running. Subject to !ERRORIGNORE.</summary>
public class MacroRuntimeException : Exception
{
    public int LineNumber { get; internal set; }
    public MacroRuntimeException(string message, int lineNumber = 0, Exception? inner = null) : base(message, inner) => LineNumber = lineNumber;
}

/// <summary>Raised by EVAL("MacroError('...')") — always stops the macro (not ignored by !ERRORIGNORE).</summary>
public sealed class MacroAbortException : MacroRuntimeException
{
    public MacroAbortException(string message, int lineNumber = 0) : base(message, lineNumber) { }
}
