﻿using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;
using Remora.Results;

namespace Octobot.Services.Profiler;

/// <summary>
/// Provides the ability to profile how long certain parts of code take to complete using <see cref="Stopwatch"/>es.
/// </summary>
/// <remarks>Resolve <see cref="ProfilerFactory"/> instead in singletons.</remarks>
public sealed class Profiler
{
    private const int MaxProfilerTime = 10; // milliseconds
    private readonly List<ProfilerEvent> _events = [];
    private readonly ILogger<Profiler> _logger;
    private int _runningStopwatches;

    public Profiler(ILogger<Profiler> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Pushes an event to the profiler.
    /// </summary>
    /// <param name="id">The ID of the event.</param>
    public void Push(string id)
    {
        _runningStopwatches++;
        _events.Add(new ProfilerEvent
        {
            Id = id,
            Stopwatch = Stopwatch.StartNew(),
            NestingLevel = _runningStopwatches - 1
        });
    }

    /// <summary>
    /// Pops the last pushed event from the profiler.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown if the profiler contains no events.</exception>
    public void Pop()
    {
        if (_runningStopwatches is 0)
        {
            throw new InvalidOperationException("Nothing to pop");
        }

        _runningStopwatches--;
        _events.FindLast(item => item.Stopwatch.IsRunning).Stopwatch.Stop();
    }

    public Result PopWithResult(Result result)
    {
        Pop();
        return result;
    }

    /// <summary>
    /// If the profiler took too long to execute, this will log a warning with per-event time usage
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown if there are stopwatches still running.</exception>
    private void Report()
    {
        var main = _events[0];
        if (main.Stopwatch.ElapsedMilliseconds < MaxProfilerTime)
        {
            return;
        }

        var unprofiled = main.Stopwatch.ElapsedMilliseconds;
        var builder = new StringBuilder().AppendLine();
        for (var i = 1; i < _events.Count; i++)
        {
            var profilerEvent = _events[i];
            builder.Append(' ', profilerEvent.NestingLevel * 4)
                .AppendLine($"{profilerEvent.Id}: {profilerEvent.Stopwatch.ElapsedMilliseconds}ms");
            unprofiled -= profilerEvent.Stopwatch.ElapsedMilliseconds;
        }

        if (unprofiled > 0)
        {
            builder.AppendLine($"<unprofiled>: {unprofiled}ms");
        }

        _logger.LogWarning("Profiler {ID} took {Elapsed} milliseconds to execute (max: {Max}ms):{Events}", main.Id,
            main.Stopwatch.ElapsedMilliseconds, MaxProfilerTime, builder.ToString());
    }

    /// <summary>
    /// <see cref="Pop"/> the profiler and <see cref="Report"/> on it afterwards.
    /// </summary>
    private void PopAndReport()
    {
        while (_runningStopwatches > 0)
        {
            Pop();
        }

        Report();
    }

    /// <summary>
    /// <see cref="PopAndReport"/> on the profiler and return a <see cref="Result{TEntity}"/>.
    /// </summary>
    /// <param name="result"></param>
    /// <returns></returns>
    public Result ReportWithResult(Result result)
    {
        PopAndReport();
        return result;
    }

    /// <summary>
    /// Calls <see cref="ReportWithResult"/> with <see cref="Result.FromSuccess"/>
    /// </summary>
    /// <returns>A successful result.</returns>
    public Result ReportWithSuccess()
    {
        return ReportWithResult(Result.FromSuccess());
    }
}
