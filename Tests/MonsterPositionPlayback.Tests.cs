using System;
using UnityEngine;

internal static class MonsterPositionPlaybackTests
{
    private static void Main()
    {
        var playback = new MonsterPositionPlayback();
        Vector3 previous = Vector3.zero;
        float smallestStep = float.MaxValue;
        float largestStep = 0;
        for (int renderFrame = 0; renderFrame <= 180; renderFrame++)
        {
            double now = renderFrame / 60d;
            if (renderFrame % 30 == 0)
                playback.Add(new Vector3((float)now * 3, (float)now * .2f, 0), 10000 + renderFrame / 30 * 500, now, 8);
            if (!playback.TrySample(now, out Vector3 position)) continue;
            if (renderFrame >= 60)
            {
                float step = position.x - previous.x;
                smallestStep = Math.Min(smallestStep, step);
                largestStep = Math.Max(largestStep, step);
                Check(Math.Abs(position.y - position.x / 15f) < .001f, "height interpolates on the slope");
            }
            previous = position;
        }
        Check(smallestStep > .04f && largestStep < .06f, "500 ms samples move continuously across packets");
        Check(playback.TryGetPrediction(out Vector3 tail, out double duration)
            && tail.x <= .30001f && tail.y == 0 && duration <= .100001, "bounded stable ground prediction");
        playback.SetPrediction(new Vector3(9 + tail.x, .6f, 0), duration);
        Check(playback.TrySample(4, out Vector3 capped) && capped.x > 9 && capped.x <= 9.30001f,
            "position holds at checked endpoint when the next packet is late");
        playback.Add(new Vector3(9, .6f, 0), 13500, 4, 8);
        Check(playback.TrySample(5, out Vector3 held) && Math.Abs(held.x - 9) < .00001f,
            "rotation or repeated position cancels prediction without extending it");

        playback.Clear();
        playback.Add(Vector3.zero, 15000, 5, 8);
        playback.Add(new Vector3(1, 0, 0), 15500, 5.5, 8);
        playback.Add(new Vector3(1, 0, 1), 16000, 6, 8);
        Check(!playback.TryGetPrediction(out _, out _), "sharp turns do not predict beyond the latest sample");
        Check(playback.TrySample(7, out Vector3 corner) && corner.z == 1, "no approved endpoint means no displacement beyond authority");
        playback.Clear();
        Check(!playback.TrySample(8, out _), "stop, attack or death reset the playback buffer");

        playback.Add(new Vector3(0, 4, 0), 20000, 9, 8);
        playback.Add(new Vector3(0, 6, 0), 20500, 9.5, 8);
        playback.TrySample(9.5, out _);
        Check(playback.TrySample(9.8, out Vector3 flight) && flight.y > 4 && flight.y < 6,
            "flight interpolates server height");
        playback.Add(new Vector3(20, 4, 0), 21000, 10, 8);
        Check(!playback.TrySample(10, out _), "large correction discards old points");
        playback.Add(new Vector3(20.2f, 4, 0), 23000, 12, 8);
        Check(!playback.TrySample(12, out _), "long gaps do not bridge old points");
        CheckArrival();
        Console.WriteLine($"PASS: monster slope, 500 ms frames, stable and bounded prediction, turns, stop, flight and corrections ({smallestStep:F3}–{largestStep:F3} m/frame).");
    }

    private static void CheckArrival()
    {
        var playback = new MonsterPositionPlayback();
        Vector3 start = new(0, 3, 0);
        Vector3 corner = new(1.5f, 4, 0);
        Vector3 end = new(1.5f, 5, 1.5f);
        playback.Add(start, 1000, 0, 8);
        playback.Add(corner, 1500, .5, 8);
        Check(playback.TrySample(.99, out Vector3 beforeStop), "patrol started playing the first segment");
        playback.SetPrediction(corner + new Vector3(.3f, 0, 0), .1);
        playback.BeginArrival(beforeStop, end, 2000, 1, 8, 3);
        Check(playback.SampleArrival(1.02).z == 0, "finish preserves the unread corner rather than cutting directly to the end");
        Vector3 mid = playback.SampleArrival(1.4);
        Check(mid.z > 0 && mid.z < end.z && mid.x == end.x && mid.y > corner.y && mid.y < end.y,
            "finish follows the last confirmed segment and preserves height");
        Check(playback.SampleArrival(2) == end && playback.SampleArrival(3) == end,
            "finish holds the exact confirmed endpoint with no extrapolation");

        playback.BeginArrival(end + Vector3.right * .3f, end, 2000, 3, 8, 3);
        Vector3 correction = playback.SampleArrival(3.05);
        Check(correction.x > end.x && correction.x < end.x + .3f && playback.SampleArrival(3.2) == end,
            "an already consumed buffer blends out residual prediction without jumping");

        playback.Clear();
        playback.BeginArrival(start, end, 0, 4, 8, 3);
        Check(playback.SampleArrival(4) == start && playback.SampleArrival(4.1).y > start.y
            && playback.SampleArrival(4.7) == end, "a missing clock or path samples blends from the displayed position");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
