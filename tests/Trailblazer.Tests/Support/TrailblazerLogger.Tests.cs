using FluentAssertions;
using GridForge;
using SwiftCollections.Diagnostics;
using System;
using System.Collections.Generic;
using Trailblazer;
using Xunit;

namespace Trailblazer.Tests.Support;

[Collection("TrailblazerLoggerCollection")]
public sealed class TrailblazerLoggerTests : IDisposable
{
    private readonly DiagnosticLevel _originalMinimumLevel = TrailblazerLogger.MinimumLevel;
    private readonly bool _originalEnableDebugLogging = TrailblazerLogger.EnableDebugLogging;
    private readonly Action<DiagnosticLevel, string, string> _originalLogHandler = TrailblazerLogger.LogHandler;
    private readonly Func<DiagnosticLevel, string, string, string> _originalFormatter = TrailblazerLogger.CustomFormatter;

    public void Dispose()
    {
        TrailblazerLogger.MinimumLevel = _originalMinimumLevel;
        TrailblazerLogger.EnableDebugLogging = _originalEnableDebugLogging;
        TrailblazerLogger.LogHandler = _originalLogHandler;
        TrailblazerLogger.CustomFormatter = _originalFormatter;
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void DebugChannel_ShouldNotEmit_WhenDebugLoggingIsDisabled()
    {
        var entries = new List<(DiagnosticLevel Level, string Message, string Source)>();
        TrailblazerLogger.MinimumLevel = DiagnosticLevel.Info;
        TrailblazerLogger.EnableDebugLogging = false;
        TrailblazerLogger.LogHandler = (level, message, source) => entries.Add((level, message, source));

        TrailblazerLogger.DebugChannel.Info($"suppressed");

        entries.Should().BeEmpty();
    }

    [Fact]
    public void DebugChannel_ShouldEmitInfo_WhenDebugLoggingIsEnabled()
    {
        var entries = new List<(DiagnosticLevel Level, string Message, string Source)>();
        TrailblazerLogger.MinimumLevel = DiagnosticLevel.Info;
        TrailblazerLogger.EnableDebugLogging = true;
        TrailblazerLogger.LogHandler = (level, message, source) => entries.Add((level, message, source));

        TrailblazerLogger.DebugChannel.Info($"visible");

        entries.Should().ContainSingle();
        entries[0].Level.Should().Be(DiagnosticLevel.Info);
        entries[0].Message.Should().Be("visible");
        entries[0].Source.Should().Contain(nameof(DebugChannel_ShouldEmitInfo_WhenDebugLoggingIsEnabled));
    }

    [Fact]
    public void ChannelWarn_ShouldNotEvaluateFormattedExpressions_WhenWarningIsDisabled()
    {
        int evaluations = 0;
        var entries = new List<(DiagnosticLevel Level, string Message, string Source)>();
        TrailblazerLogger.MinimumLevel = DiagnosticLevel.Error;
        TrailblazerLogger.LogHandler = (level, message, source) => entries.Add((level, message, source));

        TrailblazerLogger.Channel.Warn($"suppressed {Evaluate()}");

        evaluations.Should().Be(0);
        entries.Should().BeEmpty();

        string Evaluate()
        {
            evaluations++;
            return "value";
        }
    }

    [Fact]
    public void DebugChannel_ShouldNotEvaluateFormattedExpressions_WhenDebugLoggingIsDisabled()
    {
        int evaluations = 0;
        var entries = new List<(DiagnosticLevel Level, string Message, string Source)>();
        TrailblazerLogger.MinimumLevel = DiagnosticLevel.Info;
        TrailblazerLogger.EnableDebugLogging = false;
        TrailblazerLogger.LogHandler = (level, message, source) => entries.Add((level, message, source));

        TrailblazerLogger.DebugChannel.Info($"suppressed {Evaluate()}");

        evaluations.Should().Be(0);
        entries.Should().BeEmpty();

        string Evaluate()
        {
            evaluations++;
            return "value";
        }
    }

    [Fact]
    public void IsEnabled_ShouldUseMainChannelMinimumLevel()
    {
        TrailblazerLogger.MinimumLevel = DiagnosticLevel.Warning;

        TrailblazerLogger.IsEnabled(DiagnosticLevel.Info).Should().BeFalse();
        TrailblazerLogger.IsEnabled(DiagnosticLevel.Warning).Should().BeTrue();
        TrailblazerLogger.IsEnabled(DiagnosticLevel.Error).Should().BeTrue();

        TrailblazerLogger.MinimumLevel = DiagnosticLevel.None;
        TrailblazerLogger.IsEnabled(DiagnosticLevel.Error).Should().BeFalse();
    }
}

[CollectionDefinition("TrailblazerLoggerCollection", DisableParallelization = true)]
public sealed class TrailblazerLoggerCollection
{
}
