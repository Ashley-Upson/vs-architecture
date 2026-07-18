using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace StandardIo.ArchitectureDiagram.Core.Models;

public sealed record GenerationThreadMetric(
    string Stage,
    int StartManagedThreadId,
    int EndManagedThreadId,
    bool StartIsMainThread,
    bool EndIsMainThread,
    DateTimeOffset StartedAt,
    DateTimeOffset EndedAt);

public sealed class GenerationThreadTelemetrySession : IDisposable
{
    private static readonly AsyncLocal<GenerationThreadTelemetrySession?> Ambient = new();
    private readonly object gate = new();
    private readonly Func<bool> isMainThread;
    private readonly List<GenerationThreadMetric> metrics = new();
    private bool disposed;

    private GenerationThreadTelemetrySession(Func<bool> isMainThread)
    {
        this.isMainThread = isMainThread;
        Ambient.Value = this;
    }

    public static GenerationThreadTelemetrySession? Current => Ambient.Value;

    public static GenerationThreadTelemetrySession Start(Func<bool> isMainThread)
    {
        if (isMainThread is null) throw new ArgumentNullException(nameof(isMainThread));
        if (Ambient.Value is not null) throw new InvalidOperationException("A generation thread telemetry session is already active.");
        return new GenerationThreadTelemetrySession(isMainThread);
    }

    public IDisposable Measure(string stage) => new Scope(this, stage, Capture());

    public void Mark(string stage)
    {
        var point = Capture();
        Add(new GenerationThreadMetric(stage, point.ThreadId, point.ThreadId, point.IsMainThread,
            point.IsMainThread, point.Timestamp, point.Timestamp));
    }

    public IReadOnlyList<GenerationThreadMetric> Snapshot()
    {
        lock (gate) return metrics.OrderBy(item => item.StartedAt).ThenBy(item => item.Stage, StringComparer.Ordinal).ToArray();
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        if (ReferenceEquals(Ambient.Value, this)) Ambient.Value = null;
    }

    private Point Capture() => new(Thread.CurrentThread.ManagedThreadId, isMainThread(), DateTimeOffset.UtcNow);
    private void Add(GenerationThreadMetric metric) { lock (gate) metrics.Add(metric); }

    private sealed class Scope : IDisposable
    {
        private readonly GenerationThreadTelemetrySession owner;
        private readonly string stage;
        private readonly Point start;
        private bool disposed;
        public Scope(GenerationThreadTelemetrySession owner, string stage, Point start)
        { this.owner = owner; this.stage = stage; this.start = start; }
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            var end = owner.Capture();
            owner.Add(new GenerationThreadMetric(stage, start.ThreadId, end.ThreadId,
                start.IsMainThread, end.IsMainThread, start.Timestamp, end.Timestamp));
        }
    }

    private sealed record Point(int ThreadId, bool IsMainThread, DateTimeOffset Timestamp);
}
