using AVAMarble.Engine;

namespace AVAMarble.Tests;

/// <summary>How the computer's marble gets on, course by course (set MARBLE_PROBE to run it, for tuning).</summary>
public sealed class MarbleProbe(ITestOutputHelper output)
{
    [Fact]
    public void How_the_computer_rolls()
    {
        if (Environment.GetEnvironmentVariable("MARBLE_PROBE") is null) return;
        for (int course = 0; course < Courses.Count; course++)
        foreach (int seed in new[] { 1, 2, 3 })
        {
            var g = new GameEngine(seed, HighScoreTable.Load(Path.Combine(Path.GetTempPath(), "marble-probe-" + Guid.NewGuid().ToString("N")))) { Silent = true };
            g.StartGame();
            g.StartAt(course);
            g.Go();
            var why = new Dictionary<string, int>();
            var last = g.Phase;
            double maxNext = 0;
            int t = 0;
            for (; t < 60 * 120 && g.Phase is Phase.Rolling or Phase.Mishap; t++)
            {
                g.Tick(g.Pilot.Decide(g));
                if (g.Phase == Phase.Mishap && last != Phase.Mishap) why[$"{g.Mishap}@{g.Pilot.Next}"] = why.GetValueOrDefault($"{g.Mishap}@{g.Pilot.Next}") + 1;
                maxNext = Math.Max(maxNext, g.Pilot.Next);
                last = g.Phase;
            }
            output.WriteLine($"course {course} seed {seed}: {g.Phase} after {t / 60.0:0.0}s (limit {g.Course.Time}) waypoint {maxNext}/{g.Course.Path.Count - 1} at ({g.Player.X:0.0},{g.Player.Y:0.0},{g.Player.Z:0.0}) mishaps: {string.Join(", ", why.Select(kv => kv.Key + " " + kv.Value))}");
        }
    }
}
