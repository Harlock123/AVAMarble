namespace AVAMarble.Engine;

/// <summary>
/// The computer's marble: follows the course's racing line from waypoint to waypoint, at the speed
/// each stretch allows, steering back onto the line when it drifts and braking when it's going too
/// fast. It has only the same eight-way stick as a player, so it pulses it to push gently. It waits
/// for gulpers to pass. Used for the demonstration and the tests.
/// </summary>
public sealed class MarblePilot(int seed)
{
    private readonly Random _rng = new(seed);
    private int _next = 1;
    private double _sx, _sy;
    private Course? _course;
    private Marble? _marble;

    public int Next => _next;

    public Controls Decide(GameEngine g)
    {
        var m = g.Player;
        var path = g.Course.Path;
        if (!ReferenceEquals(_course, g.Course)) { _course = g.Course; _next = 1; }
        if (!ReferenceEquals(_marble, m)) { _marble = m; _next = Nearest(path, m); }
        if (g.Phase != Phase.Rolling || path.Count < 2) return default;
        var pos = new Vec(m.X, m.Y);
        // On to the next waypoint once this one is reached (or passed).
        while (_next < path.Count - 1)
        {
            var a = P(path[_next - 1]);
            var b = P(path[_next]);
            var seg = b - a;
            double t = seg.Length < 1e-6 ? 1 : (pos - a).Dot(seg) / seg.Dot(seg);
            if ((pos - b).Length < 0.7 || t > 1) _next++;
            else break;
        }
        var from = P(path[_next - 1]);
        var to = P(path[_next]);
        var line = to - from;
        double len = line.Length;
        var dir = len < 1e-6 ? default : line * (1 / len);
        // The nearest point on the line, to steer back towards.
        double along = Math.Clamp((pos - from).Dot(dir), 0, len);
        var onLine = from + dir * along;
        var toTarget = to - pos;
        double speed = path[_next].Speed;
        // Slow down for the waypoint if the next stretch turns sharply.
        if (_next + 1 < path.Count)
        {
            var after = P(path[_next + 1]) - to;
            if (after.Length > 1e-6 && dir.Dot(after * (1 / after.Length)) < 0.5)
                speed = Math.Min(speed, Math.Max(1.8, toTarget.Length * 1.2));
        }
        if (GulperAhead(g, pos, dir)) speed = 0;
        var aim = toTarget.Length < 1e-6 ? dir : toTarget * (1 / toTarget.Length);
        var want = aim * speed + (onLine - pos) * 2.5;
        var accel = (want - m.V) * 3.5;
        bool brake = m.Speed > speed + 1.6 || speed == 0 && m.Speed > 0.5;
        // From a push across the course to the stick: pulse it to push gently.
        double cx = Math.Clamp(accel.Dot(Field.ScreenRight) / GameEngine.Push, -1, 1);
        double cy = Math.Clamp(accel.Dot(Field.ScreenDown) / GameEngine.Push, -1, 1);
        return new Controls(Pulse(ref _sx, cx), Pulse(ref _sy, cy), brake);
    }

    private static int Pulse(ref double acc, double v)
    {
        acc += v;
        if (acc > 0.5) { acc -= 1; return 1; }
        if (acc < -0.5) { acc += 1; return -1; }
        return 0;
    }

    private static Vec P(Waypoint w) => new(w.X, w.Y);

    /// <summary>The stretch of the line the marble is nearest, after a new marble appears.</summary>
    private static int Nearest(List<Waypoint> path, Marble m)
    {
        int best = 1;
        double bestD = double.MaxValue;
        var pos = new Vec(m.X, m.Y);
        for (int i = 1; i < path.Count; i++)
        {
            var a = P(path[i - 1]);
            var b = P(path[i]);
            var seg = b - a;
            double t = seg.Dot(seg) < 1e-9 ? 0 : Math.Clamp((pos - a).Dot(seg) / seg.Dot(seg), 0, 1);
            double d = (a + seg * t - pos).Length;
            if (d < bestD) { bestD = d; best = i; }
        }
        return best;
    }

    private static bool GulperAhead(GameEngine g, Vec pos, Vec dir)
    {
        foreach (var gu in g.Gulpers)
        {
            var at = new Vec(gu.X, gu.Y);
            var rel = at - pos;
            double ahead = rel.Dot(dir);
            if (ahead > -0.3 && ahead < 2.2 && rel.Length < 2.3) return true;
        }
        return false;
    }
}
