using SwiftCollections.Diagnostics;
using System;
using System.IO;
using System.Runtime.CompilerServices;

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
    private static readonly DiagnosticChannel _channel = new("Trailblazer");
    private static Action<DiagnosticLevel, string, string> _logHandler = DefaultLogHandler;
    private static Func<DiagnosticLevel, string, string, string> _customFormatter = DefaultLogFormatter;

    static TrailblazerLogger()
    {
        _channel.MinimumLevel = DiagnosticLevel.Warning;
        _channel.Sink = static (in DiagnosticEvent diagnostic) =>
        {
            _logHandler(
                diagnostic.Level,
                diagnostic.Message,
                string.IsNullOrWhiteSpace(diagnostic.Source) ? diagnostic.Channel : diagnostic.Source);
        };
    }

    /// <summary>
    /// Gets or sets a value indicating whether verbose debug diagnostics should be emitted.
    /// </summary>
    public static bool EnableDebugLogging { get; set; }

    /// <summary>
    /// Gets or sets the minimum severity required for non-debug diagnostics to be emitted.
    /// </summary>
    public static DiagnosticLevel MinimumLevel
    {
        get => _channel.MinimumLevel;
        set => _channel.MinimumLevel = value;
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
    /// Gets a value indicating whether verbose debug diagnostics are currently enabled.
    /// </summary>
    public static bool IsDebugEnabled => EnableDebugLogging && IsEnabled(DiagnosticLevel.Info);

    /// <summary>
    /// Determines whether diagnostics at the specified level are enabled.
    /// </summary>
    /// <param name="level">The level to evaluate.</param>
    /// <returns><see langword="true"/> when the level is enabled; otherwise, <see langword="false"/>.</returns>
    public static bool IsEnabled(DiagnosticLevel level) => _channel.IsEnabled(level);

    /// <summary>
    /// Logs an informational diagnostic message.
    /// </summary>
    public static void Info(
        string message,
        [CallerMemberName] string method = "",
        [CallerFilePath] string filePath = "")
        => Write(DiagnosticLevel.Info, message, method, filePath);

    /// <summary>
    /// Logs a warning diagnostic message.
    /// </summary>
    public static void Warn(
        string message,
        [CallerMemberName] string method = "",
        [CallerFilePath] string filePath = "")
        => Write(DiagnosticLevel.Warning, message, method, filePath);

    /// <summary>
    /// Logs an error diagnostic message.
    /// </summary>
    public static void Error(
        string message,
        [CallerMemberName] string method = "",
        [CallerFilePath] string filePath = "")
        => Write(DiagnosticLevel.Error, message, method, filePath);

    /// <summary>
    /// Logs a verbose debug diagnostic message when debug diagnostics are enabled.
    /// </summary>
    public static void Debug(
        string message,
        [CallerMemberName] string method = "",
        [CallerFilePath] string filePath = "")
    {
        if (!IsDebugEnabled)
            return;

        Write(DiagnosticLevel.Info, message, method, filePath);
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

    private static void Write(
        DiagnosticLevel level,
        string message,
        string method,
        string filePath)
    {
        if (!_channel.IsEnabled(level))
            return;

        _channel.Write(level, message, BuildSource(method, filePath));
    }

    private static string BuildSource(string method, string filePath)
    {
        string typeName = Path.GetFileNameWithoutExtension(filePath);
        if (string.IsNullOrWhiteSpace(typeName))
            return method;

        return string.IsNullOrWhiteSpace(method)
            ? typeName
            : $"{typeName}.{method}";
    }
}
