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
    private static readonly DiagnosticChannel _channel = CreateChannel();
    private static readonly DiagnosticChannel _debugChannel = CreateChannel();
    private static Action<DiagnosticLevel, string, string> _logHandler = DefaultLogHandler;
    private static Func<DiagnosticLevel, string, string, string> _customFormatter = DefaultLogFormatter;
    private static bool _enableDebugLogging;

    static TrailblazerLogger()
    {
        RefreshDebugMinimumLevel();
    }

    /// <summary>
    /// Gets or sets a value indicating whether verbose debug diagnostics should be emitted.
    /// </summary>
    public static bool EnableDebugLogging
    {
        get => _enableDebugLogging;
        set
        {
            _enableDebugLogging = value;
            RefreshDebugMinimumLevel();
        }
    }

    /// <summary>
    /// Gets the diagnostic channel used for Trailblazer warnings and errors.
    /// </summary>
    public static DiagnosticChannel Channel => _channel;

    /// <summary>
    /// Gets the diagnostic channel used for verbose debug diagnostics.
    /// </summary>
    public static DiagnosticChannel DebugChannel => _debugChannel;

    /// <summary>
    /// Gets or sets the minimum severity required for non-debug diagnostics to be emitted.
    /// </summary>
    public static DiagnosticLevel MinimumLevel
    {
        get => _channel.MinimumLevel;
        set
        {
            _channel.MinimumLevel = value;
            RefreshDebugMinimumLevel();
        }
    }

    /// <summary>
    /// Gets or sets the delegate used to write formatted log messages.
    /// Assigning <see langword="null"/> restores <see cref="DefaultLogHandler"/>.
    /// </summary>
    public static Action<DiagnosticLevel, string, string> LogHandler
    {
        get => _logHandler;
        set => _logHandler = value ?? DefaultLogHandler;
    }

    /// <summary>
    /// Gets or sets the formatter used to transform log arguments into a final log entry.
    /// Assigning <see langword="null"/> restores <see cref="DefaultLogFormatter"/>.
    /// </summary>
    public static Func<DiagnosticLevel, string, string, string> CustomFormatter
    {
        get => _customFormatter;
        set => _customFormatter = value ?? DefaultLogFormatter;
    }

    /// <summary>
    /// The default handler for Trailblazer log messages.
    /// </summary>
    public static void DefaultLogHandler(DiagnosticLevel level, string message, string source)
    {
        string entry = CustomFormatter(level, message, source);
        if (level == DiagnosticLevel.Error)
            Console.Error.WriteLine(entry);
        else
            Console.WriteLine(entry);
    }

    /// <summary>
    /// Formats a Trailblazer log entry using a deterministic, source-first layout.
    /// </summary>
    public static string DefaultLogFormatter(DiagnosticLevel level, string message, string source)
    {
        if (string.IsNullOrWhiteSpace(source))
            return $"[{level}] Trailblazer: {message}";

        return $"[{level}] Trailblazer.{source}: {message}";
    }

    private static DiagnosticChannel CreateChannel()
    {
        return new DiagnosticChannel("Trailblazer")
        {
            MinimumLevel = DiagnosticLevel.Warning,
            Sink = HandleDiagnosticEvent
        };
    }

    private static void HandleDiagnosticEvent(in DiagnosticEvent diagnostic)
    {
        _logHandler(
            diagnostic.Level,
            diagnostic.Message,
            string.IsNullOrWhiteSpace(diagnostic.Source) ? diagnostic.Channel : diagnostic.Source);
    }

    private static void RefreshDebugMinimumLevel()
    {
        _debugChannel.MinimumLevel = _enableDebugLogging
            ? _channel.MinimumLevel
            : DiagnosticLevel.None;
    }
}
