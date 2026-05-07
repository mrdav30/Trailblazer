using SwiftCollections.Diagnostics;
using System;

namespace Trailblazer;

/// <summary>
/// Provides centralized diagnostics for Trailblazer runtime code.
/// </summary>
/// <remarks>
/// Warning and error diagnostics are controlled through <see cref="MinimumLevel"/>.
/// Trace-style diagnostics that were previously debug-only are additionally gated by
/// <see cref="EnableDebugLogging"/> so hosts can opt into verbose runtime output explicitly.
/// </remarks>
public static class TrailblazerLogger
{
    private static readonly TrailblazerDiagnosticLogger _logger = new();

    /// <summary>
    /// Gets or sets a value indicating whether verbose debug diagnostics should be emitted.
    /// </summary>
    public static bool EnableDebugLogging
    {
        get => _logger.EnableDebugLogging;
        set => _logger.EnableDebugLogging = value;
    }

    /// <summary>
    /// Gets the diagnostic channel used for Trailblazer warnings and errors.
    /// </summary>
    public static DiagnosticChannel Channel => _logger.Channel;

    /// <summary>
    /// Gets the diagnostic channel used for verbose debug diagnostics.
    /// </summary>
    public static DiagnosticChannel DebugChannel => _logger.DebugChannel;

    /// <summary>
    /// Gets or sets the minimum severity required for non-debug diagnostics to be emitted.
    /// </summary>
    public static DiagnosticLevel MinimumLevel
    {
        get => _logger.MinimumLevel;
        set => _logger.MinimumLevel = value;
    }

    /// <summary>
    /// Gets or sets the delegate used to write formatted log messages.
    /// Assigning <see langword="null"/> restores <see cref="DefaultLogHandler"/>.
    /// </summary>
    public static Action<DiagnosticLevel, string, string> LogHandler
    {
        get => _logger.LogHandler;
        set => _logger.LogHandler = value;
    }

    /// <summary>
    /// Gets or sets the formatter used to transform log arguments into a final log entry.
    /// Assigning <see langword="null"/> restores <see cref="DefaultLogFormatter"/>.
    /// </summary>
    public static Func<DiagnosticLevel, string, string, string> CustomFormatter
    {
        get => _logger.CustomFormatter;
        set => _logger.CustomFormatter = value;
    }

    /// <summary>
    /// Determines whether non-debug diagnostics at the specified level are currently enabled.
    /// </summary>
    /// <param name="level">The diagnostic level to evaluate.</param>
    /// <returns><see langword="true"/> when messages at <paramref name="level"/> will be emitted; otherwise, <see langword="false"/>.</returns>
    public static bool IsEnabled(DiagnosticLevel level)
    {
        return _logger.IsEnabled(level);
    }

    /// <summary>
    /// The default handler for Trailblazer log messages.
    /// </summary>
    public static void DefaultLogHandler(DiagnosticLevel level, string message, string source)
    {
        _logger.DefaultLogHandler(level, message, source);
    }

    /// <summary>
    /// Formats a Trailblazer log entry using a deterministic, source-first layout.
    /// </summary>
    public static string DefaultLogFormatter(DiagnosticLevel level, string message, string source)
    {
        return _logger.DefaultLogFormatter(level, message, source);
    }

    private sealed class TrailblazerDiagnosticLogger : DiagnosticLogger
    {
        public TrailblazerDiagnosticLogger()
            : base("Trailblazer")
        {
        }
    }
}
