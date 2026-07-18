using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;

namespace StandardIo.ArchitectureDiagram.Core.Models;

public sealed record PerformancePhaseMetric(
    string Path,
    string Phase,
    long InclusiveMicroseconds,
    long ExclusiveMicroseconds,
    int InvocationCount,
    long InputNodes,
    long InputRoutes,
    long InputSegments,
    long OutputObjects,
    int? LayoutRevision,
    int? RouteRevision);

public sealed record PerformanceCounterMetric(string Name, long Value);

public sealed record GenerationPerformanceReport(
    long ElapsedMilliseconds,
    IReadOnlyList<PerformancePhaseMetric> Phases,
    IReadOnlyList<PerformanceCounterMetric> Counters);

public sealed class GenerationPerformanceSession : IDisposable
{
    private static readonly AsyncLocal<GenerationPerformanceSession?> Ambient = new();
    private readonly object sync = new();
    private readonly Dictionary<string, MutablePhase> phases = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> counters = new(StringComparer.Ordinal);
    private readonly Stopwatch timer = Stopwatch.StartNew();
    private Frame? current;
    private bool disposed;

    private GenerationPerformanceSession(int serializationRepeatCount)
    {
        SerializationRepeatCount = Math.Max(0, serializationRepeatCount);
        Ambient.Value = this;
    }

    public static GenerationPerformanceSession? Current => Ambient.Value;
    public int SerializationRepeatCount { get; }

    public static GenerationPerformanceSession Start(int serializationRepeatCount = 0)
    {
        if (Ambient.Value is not null)
        {
            throw new InvalidOperationException("A generation performance session is already active.");
        }

        return new GenerationPerformanceSession(serializationRepeatCount);
    }

    public IDisposable Measure(
        string phase,
        int inputNodes = 0,
        int inputRoutes = 0,
        int inputSegments = 0,
        int outputObjects = 0,
        int? layoutRevision = null,
        int? routeRevision = null)
    {
        var parent = current;
        var path = parent is null ? phase : $"{parent.Path} > {phase}";
        var frame = new Frame(path, phase, parent, Stopwatch.GetTimestamp(), inputNodes, inputRoutes,
            inputSegments, outputObjects, layoutRevision, routeRevision);
        current = frame;
        return new Scope(this, frame);
    }

    public void Increment(string name, long amount = 1)
    {
        lock (sync)
        {
            counters[name] = counters.TryGetValue(name, out var value) ? value + amount : amount;
        }
    }

    public GenerationPerformanceReport Snapshot()
    {
        lock (sync)
        {
            return new GenerationPerformanceReport(
                timer.ElapsedMilliseconds,
                phases.Values
                    .OrderBy(phase => phase.Path, StringComparer.Ordinal)
                    .Select(phase => phase.ToMetric())
                    .ToArray(),
                counters.OrderBy(item => item.Key, StringComparer.Ordinal)
                    .Select(item => new PerformanceCounterMetric(item.Key, item.Value))
                    .ToArray());
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        timer.Stop();
        if (ReferenceEquals(Ambient.Value, this))
        {
            Ambient.Value = null;
        }
    }

    private void Complete(Frame frame)
    {
        var elapsedTicks = Stopwatch.GetTimestamp() - frame.StartTimestamp;
        current = frame.Parent;
        if (frame.Parent is not null)
        {
            frame.Parent.ChildTicks += elapsedTicks;
        }

        lock (sync)
        {
            if (!phases.TryGetValue(frame.Path, out var phase))
            {
                phase = new MutablePhase(frame.Path, frame.Phase);
                phases.Add(frame.Path, phase);
            }

            phase.Add(frame, elapsedTicks);
        }
    }

    private sealed class Scope : IDisposable
    {
        private readonly GenerationPerformanceSession owner;
        private readonly Frame frame;
        private bool disposed;

        public Scope(GenerationPerformanceSession owner, Frame frame)
        {
            this.owner = owner;
            this.frame = frame;
        }

        public void Dispose()
        {
            if (!disposed)
            {
                disposed = true;
                owner.Complete(frame);
            }
        }
    }

    private sealed class Frame
    {
        public Frame(string path, string phase, Frame? parent, long startTimestamp, int inputNodes,
            int inputRoutes, int inputSegments, int outputObjects, int? layoutRevision, int? routeRevision)
        {
            Path = path;
            Phase = phase;
            Parent = parent;
            StartTimestamp = startTimestamp;
            InputNodes = inputNodes;
            InputRoutes = inputRoutes;
            InputSegments = inputSegments;
            OutputObjects = outputObjects;
            LayoutRevision = layoutRevision;
            RouteRevision = routeRevision;
        }

        public string Path { get; }
        public string Phase { get; }
        public Frame? Parent { get; }
        public long StartTimestamp { get; }
        public long ChildTicks { get; set; }
        public int InputNodes { get; }
        public int InputRoutes { get; }
        public int InputSegments { get; }
        public int OutputObjects { get; }
        public int? LayoutRevision { get; }
        public int? RouteRevision { get; }
    }

    private sealed class MutablePhase
    {
        private long inclusiveTicks;
        private long exclusiveTicks;
        private int invocationCount;
        private long inputNodes;
        private long inputRoutes;
        private long inputSegments;
        private long outputObjects;
        private int? layoutRevision;
        private int? routeRevision;

        public MutablePhase(string path, string phase)
        {
            Path = path;
            Phase = phase;
        }

        public string Path { get; }
        public string Phase { get; }

        public void Add(Frame frame, long elapsedTicks)
        {
            inclusiveTicks += elapsedTicks;
            exclusiveTicks += Math.Max(0, elapsedTicks - frame.ChildTicks);
            invocationCount++;
            inputNodes += frame.InputNodes;
            inputRoutes += frame.InputRoutes;
            inputSegments += frame.InputSegments;
            outputObjects += frame.OutputObjects;
            layoutRevision = Merge(layoutRevision, frame.LayoutRevision);
            routeRevision = Merge(routeRevision, frame.RouteRevision);
        }

        public PerformancePhaseMetric ToMetric() =>
            new(Path, Phase, ToMicroseconds(inclusiveTicks), ToMicroseconds(exclusiveTicks), invocationCount,
                inputNodes, inputRoutes, inputSegments, outputObjects, layoutRevision, routeRevision);

        private static int? Merge(int? current, int? value) =>
            current is null ? value : value is null || current == value ? current : -1;

        private static long ToMicroseconds(long ticks) =>
            (long)(ticks * 1_000_000d / Stopwatch.Frequency);
    }
}

internal static class PerformanceAudit
{
    public static IDisposable Measure(
        string phase,
        int inputNodes = 0,
        int inputRoutes = 0,
        int inputSegments = 0,
        int outputObjects = 0,
        int? layoutRevision = null,
        int? routeRevision = null) =>
        new CombinedScope(
            GenerationPerformanceSession.Current?.Measure(
                phase, inputNodes, inputRoutes, inputSegments, outputObjects, layoutRevision, routeRevision),
            GenerationThreadTelemetrySession.Current?.Measure(phase));

    public static void Increment(string name, long amount = 1) =>
        GenerationPerformanceSession.Current?.Increment(name, amount);

    private sealed class EmptyScope : IDisposable
    {
        public static EmptyScope Instance { get; } = new();
        public void Dispose() { }
    }

    private sealed class CombinedScope : IDisposable
    {
        private readonly IDisposable performance;
        private readonly IDisposable thread;
        public CombinedScope(IDisposable? performance, IDisposable? thread)
        {
            this.performance = performance ?? EmptyScope.Instance;
            this.thread = thread ?? EmptyScope.Instance;
        }
        public void Dispose()
        {
            thread.Dispose();
            performance.Dispose();
        }
    }
}
