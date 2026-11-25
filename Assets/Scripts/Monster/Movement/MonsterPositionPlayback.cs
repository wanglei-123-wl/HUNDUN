using System;
using System.Collections.Generic;
using UnityEngine;

internal sealed class MonsterPositionPlayback
{
    private const int BufferCapacity = 12;
    private const long MaximumSampleGapMs = 1250;
    internal const double MaximumPredictionSeconds = .1;
    internal const float MaximumPredictionDistance = .3f;

    private readonly List<(Vector3 Position, long Time)> samples = new(BufferCapacity);
    private double receivedAt;
    private double intervalMs;
    private double cursorMs;
    private double renderedAt;
    private bool started;
    private Vector3 prediction;
    private double predictionSeconds;
    private bool predictionChecked;
    private Vector3 arrivalFrom;
    private Vector3 arrivalEnd;
    private double arrivalStartedAt;
    private double arrivalDuration;

    internal bool NeedsPrediction => !predictionChecked && samples.Count >= 3
        && cursorMs > samples[samples.Count - 1].Time
        && cursorMs < samples[samples.Count - 1].Time + MaximumPredictionSeconds * 1000;

    internal void Clear()
    {
        samples.Clear();
        intervalMs = 0;
        started = false;
        predictionSeconds = 0;
        predictionChecked = false;
    }

    internal void Add(Vector3 position, long serverTime, double now, float snapDistance)
    {
        if (serverTime <= 0) return;
        if (samples.Count > 0)
        {
            var last = samples[samples.Count - 1];
            if (serverTime < last.Time) return;
            predictionSeconds = 0;
            if ((position - last.Position).sqrMagnitude < .000001f)
            {
                predictionChecked = true;
                return;
            }
            if (serverTime == last.Time)
            {
                samples[samples.Count - 1] = (position, serverTime);
                predictionChecked = true;
                return;
            }
            if (serverTime - last.Time > MaximumSampleGapMs || Vector3.Distance(position, last.Position) >= snapDistance)
                Clear();
            else
            {
                double gap = serverTime - last.Time;
                intervalMs = intervalMs == 0 ? gap : intervalMs * .8 + gap * .2;
            }
        }
        if (samples.Count == BufferCapacity) samples.RemoveAt(0);
        samples.Add((position, serverTime));
        receivedAt = now;
        predictionChecked = false;
    }

    internal bool TryGetPrediction(out Vector3 displacement, out double seconds)
    {
        displacement = Vector3.zero;
        seconds = 0;
        predictionChecked = true;
        if (samples.Count < 3) return false;
        var end = samples[samples.Count - 1];
        var middle = samples[samples.Count - 2];
        var start = samples[samples.Count - 3];
        Vector3 direction = end.Position - middle.Position;
        Vector3 previousDirection = middle.Position - start.Position;
        direction.y = previousDirection.y = 0;
        double sampleSeconds = (end.Time - middle.Time) * .001;
        if (sampleSeconds < .025 || sampleSeconds > .65 || middle.Time - start.Time > 650
            || direction.sqrMagnitude < .0004f || previousDirection.sqrMagnitude < .0004f
            || Vector3.Dot(direction.normalized, previousDirection.normalized) < .95f) return false;

        double speed = direction.magnitude / sampleSeconds;
        seconds = Math.Min(MaximumPredictionSeconds, MaximumPredictionDistance / speed);
        displacement = direction * (float)(seconds / sampleSeconds);
        return true;
    }

    internal void SetPrediction(Vector3 checkedEnd, double duration)
    {
        prediction = checkedEnd;
        predictionSeconds = duration;
    }

    internal void BeginArrival(Vector3 displayed, Vector3 position, long serverTime, double now, float snapDistance,
        float speed)
    {
        arrivalEnd = position;
        arrivalDuration = 0;
        if (TryFinishAt(position, serverTime, now, snapDistance)) return;

        // A played-out buffer or an active short path has no unread samples to finish.
        Clear();
        arrivalFrom = displayed;
        arrivalStartedAt = now;
        arrivalDuration = Math.Max(.1, Math.Min(.6, Vector3.Distance(displayed, position) / Math.Max(1f, speed)));
    }

    internal Vector3 SampleArrival(double now)
    {
        if (arrivalDuration > 0)
            return Vector3.Lerp(arrivalFrom, arrivalEnd, (float)((now - arrivalStartedAt) / arrivalDuration));
        return TrySample(now, out Vector3 position) ? position : arrivalEnd;
    }

    private bool TryFinishAt(Vector3 position, long serverTime, double now, float snapDistance)
    {
        if (!started || samples.Count < 2 || serverTime <= 0) return false;
        Add(position, serverTime, now, snapDistance);
        predictionSeconds = 0;
        predictionChecked = true;
        return samples.Count >= 2 && cursorMs < samples[samples.Count - 1].Time
            && (samples[samples.Count - 1].Position - position).sqrMagnitude < .000001f;
    }

    internal bool TrySample(double now, out Vector3 position)
    {
        position = Vector3.zero;
        if (samples.Count < 2) return false;
        var latest = samples[samples.Count - 1];
        double delay = Math.Min(750, Math.Max(100, intervalMs * 1.1 + 20));
        double desired = latest.Time + (now - receivedAt) * 1000 - delay;
        if (!started)
        {
            cursorMs = Math.Max(samples[0].Time, Math.Min(desired, latest.Time));
            started = true;
        }
        else
        {
            double step = Math.Max(0, (now - renderedAt) * 1000);
            double correction = Math.Max(-step * .15, Math.Min(step * .15, desired - cursorMs - step));
            cursorMs = Math.Max(cursorMs, Math.Min(latest.Time + MaximumPredictionSeconds * 1000, cursorMs + step + correction));
        }
        renderedAt = now;
        while (samples.Count > 3 && cursorMs >= samples[1].Time) samples.RemoveAt(0);
        for (int index = 1; index < samples.Count; index++)
        {
            var after = samples[index];
            if (cursorMs >= after.Time) continue;
            var before = samples[index - 1];
            position = Vector3.Lerp(before.Position, after.Position, (float)((cursorMs - before.Time) / (after.Time - before.Time)));
            return true;
        }
        position = predictionSeconds > 0
            ? Vector3.Lerp(latest.Position, prediction, (float)((cursorMs - latest.Time) * .001 / predictionSeconds))
            : latest.Position;
        return true;
    }
}
