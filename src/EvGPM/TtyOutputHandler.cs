using System.Text;

namespace EvGPM;

/// <summary>
/// Handles writing ANSI escape sequences to the TTY
/// </summary>
public class TtyOutputHandler : IDisposable
{
    private readonly Stream _outputStream;
    private readonly StreamWriter _writer;
    private bool _disposed = false;

    public TtyOutputHandler(string? ttyPath = null)
    {
        // Default to stdout if no TTY specified
        if (string.IsNullOrEmpty(ttyPath))
        {
            _outputStream = Console.OpenStandardOutput();
        }
        else
        {
            // Open the specified TTY device
            _outputStream = new FileStream(ttyPath, FileMode.Open, FileAccess.Write, FileShare.ReadWrite);
        }

        _writer = new StreamWriter(_outputStream, Encoding.UTF8)
        {
            AutoFlush = true
        };
    }

    /// <summary>
    /// Write an ANSI mouse escape sequence to the TTY
    /// </summary>
    public void WriteMouseSequence(string ansiSequence)
    {
        if (string.IsNullOrEmpty(ansiSequence))
            return;

        try
        {
            _writer.Write(ansiSequence);
            _writer.Flush();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error writing to TTY: {ex.Message}");
        }
    }

    /// <summary>
    /// Write raw text to the TTY
    /// </summary>
    public void Write(string text)
    {
        try
        {
            _writer.Write(text);
            _writer.Flush();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error writing to TTY: {ex.Message}");
        }
    }

    /// <summary>
    /// Check if the output stream is valid
    /// </summary>
    public bool IsValid()
    {
        try
        {
            return _outputStream.CanWrite;
        }
        catch
        {
            return false;
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _writer?.Dispose();
            _outputStream?.Dispose();
            _disposed = true;
        }
    }
}
