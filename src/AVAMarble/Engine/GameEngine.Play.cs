namespace AVAMarble.Engine;

public sealed partial class GameEngine
{
    // How marbles roll.
    public const double Push = 10, SlopePull = 11, Gravity = 32, Friction = 0.45, BrakeFriction = 5, MaxSpeed = 11;
    /// <summary>A rise bigger than this is a wall; a drop bigger than this leaves the floor.</summary>
    public const double StepUp = 0.3, StepDown = 0.3;
    /// <summary>Fall further than this and the marble breaks.</summary>
    public const double BreakHeight = 3.2;
    public const double MishapSeconds = 1.6;

    public Course Course { get; private set; } = Courses.Make(0);
    public int CourseIndex { get; private set; }
    public Marble Player { get; private set; } = new();
    public List<Marble> Steelies { get; } = [];
    public List<Gulper> Gulpers { get; } = [];
    public int Score { get; internal set; }
    public double TimeLeft { get; internal set; }
    /// <summary>Seconds carried over from the course before.</summary>
    public double Carried { get; private set; }
    public int FinishBonus { get; private set; }
    /// <summary>The clock for waves and wobbles.</summary>
    public double Time { get; private set; }
    public string? Mishap { get; private set; }
    /// <summary>The computer's marble, for the demonstration (and the tests).</summary>
    public MarblePilot Pilot { get; private set; } = new(1);

    private (double X, double Y, double Z) _safe;
    private double _safeTimer;
    private int _lastTick;

    /// <summary>Test hook: no steelies or gulpers.</summary>
    internal bool Calm { get; set; }

    private void NewGame(int course)
    {
        _rng = new Random(_seed + (int)(StateTime * 997));
        Score = 0;
        Carried = 0;
        Effects.Clear();
        StartCourse(course);
    }

    private void StartCourse(int index)
    {
        CourseIndex = index;
        Course = Courses.Make(index);
        TimeLeft = Course.Time + Carried;
        double z = Course.Floor(Course.StartX, Course.StartY) ?? 0;
        Player = new Marble { X = Course.StartX, Y = Course.StartY, Z = z };
        _safe = (Player.X, Player.Y, Player.Z);
        Steelies.Clear();
        Gulpers.Clear();
        if (!Calm)
        {
            foreach (var (x, y) in Course.Steelies)
            {
                double sz = Course.Floor(x, y) ?? 0;
                Steelies.Add(new Marble { Steelie = true, X = x, Y = y, Z = sz, HomeX = x, HomeY = y, HomeZ = sz });
            }
            foreach (var (ax, ay, bx, by) in Course.Gulpers) Gulpers.Add(new Gulper { AX = ax, AY = ay, BX = bx, BY = by, X = ax, Y = ay });
        }
        Pilot = new MarblePilot(_rng.Next());
        Mishap = null;
        _lastTick = 99;
        SetPhase(Phase.CourseStart);
    }

    internal void AddScore(int points, double x, double y, double z)
    {
        Score += points;
        Effects.Add(new Effect { Kind = EffectKind.Score, X = x, Y = y, Z = z, Text = points.ToString(), Life = 1, Colour = 0xFFFFE14A });
    }

    // ───────────────────────────── the step ─────────────────────────────

    private void Simulate(double dt, Controls c, bool startPressed)
    {
        PhaseTime += dt;
        Time += dt;
        switch (Phase)
        {
            case Phase.CourseStart:
                int count = 3 - (int)PhaseTime;
                if (PhaseTime >= 0.5 && count != _lastTick && count > 0) { _lastTick = count; Play(Sfx.Count); }
                if (PhaseTime > 3.5)
                {
                    SetPhase(Phase.Rolling);
                    Play(Sfx.Go);
                    Music(true);
                    Say("GO!", 0.8);
                }
                return;
            case Phase.Finish:
                if (PhaseTime > 4 || startPressed && PhaseTime > 1.5) NextCourse();
                return;
            case Phase.TimeUp:
                if (PhaseTime > 3) GameOver();
                return;
            case Phase.Victory:
                if (PhaseTime > 5) GameOver();
                return;
        }
        Clock(dt);
        if (Phase is Phase.TimeUp) return;
        if (Phase == Phase.Mishap)
        {
            if (PhaseTime > MishapSeconds) Respawn();
        }
        else
        {
            var push = Steer(c);
            for (int i = 0; i < 2; i++)
            {
                Roll(Player, push, dt / 2, c.Brake);
                if (Phase != Phase.Rolling) break;
            }
            if (Phase == Phase.Rolling) Ground(Player);
            if (Phase == Phase.Rolling) RememberSafe(dt);
        }
        MoveSteelies(dt);
        MoveGulpers(dt);
        if (Phase == Phase.Rolling) Bumps();
    }

    private void Clock(double dt)
    {
        TimeLeft -= dt;
        int whole = (int)Math.Ceiling(TimeLeft);
        if (whole <= 10 && whole != _lastTick && whole > 0) { _lastTick = whole; Play(Sfx.Tick); }
        if (TimeLeft <= 0)
        {
            TimeLeft = 0;
            Music(false);
            Play(Sfx.TimeUp);
            Say("TIME'S UP!", 3);
            SetPhase(Phase.TimeUp);
        }
    }

    /// <summary>The stick, turned from screen directions into a push across the course.</summary>
    public static Vec Steer(Controls c)
    {
        var v = Field.ScreenRight * c.Dx + Field.ScreenDown * c.Dy;
        double len = v.Length;
        return len < 1e-9 ? default : v * (Push / len);
    }

    // ───────────────────────────── rolling ─────────────────────────────

    /// <summary>
    /// One step of a marble's motion: pushed, pulled downhill, slowed by friction, bounced off walls,
    /// dropped off ledges, and landed (or broken).
    /// </summary>
    internal void Roll(Marble m, Vec push, double dt, bool brake = false)
    {
        var course = Course;
        if (!m.Air)
        {
            var slope = course.Slope(m.X, m.Y, Time);
            m.Vx += (push.X - SlopePull * slope.X) * dt;
            m.Vy += (push.Y - SlopePull * slope.Y) * dt;
            double f = 1 - (brake ? BrakeFriction : Friction) * dt;
            m.Vx *= f;
            m.Vy *= f;
        }
        else
        {
            m.Vx += push.X * 0.25 * dt;
            m.Vy += push.Y * 0.25 * dt;
        }
        double speed = m.Speed;
        if (speed > MaxSpeed) { m.Vx *= MaxSpeed / speed; m.Vy *= MaxSpeed / speed; }

        // Across, then along, each stopped by a wall (a floor higher than the marble).
        double nx = m.X + m.Vx * dt;
        if (course.Floor(nx, m.Y, Time) is { } fx && fx > m.Z + (m.Air ? 0.02 : StepUp))
        {
            if (Math.Abs(m.Vx) > 1.2 && !m.Steelie) Play(Sfx.Bump);
            m.Vx = -m.Vx * 0.45;
        }
        else m.X = nx;
        double ny = m.Y + m.Vy * dt;
        if (course.Floor(m.X, ny, Time) is { } fy && fy > m.Z + (m.Air ? 0.02 : StepUp))
        {
            if (Math.Abs(m.Vy) > 1.2 && !m.Steelie) Play(Sfx.Bump);
            m.Vy = -m.Vy * 0.45;
        }
        else m.Y = ny;
        m.RollX += m.Vx * dt;
        m.RollY += m.Vy * dt;

        // Up and down.
        var floor = course.Floor(m.X, m.Y, Time);
        if (!m.Air)
        {
            if (floor is null || floor < m.Z - StepDown)
            {
                // Off the edge: falling, at whatever speed the slope was carrying it down.
                var slope = course.Slope(m.X - m.Vx * dt, m.Y - m.Vy * dt, Time);
                m.Air = true;
                m.Vz = Math.Min(0, slope.Dot(m.V));
                m.PeakZ = m.Z;
            }
            else m.Z = floor.Value;
        }
        if (m.Air)
        {
            m.Vz -= Gravity * dt;
            m.Z += m.Vz * dt;
            m.PeakZ = Math.Max(m.PeakZ, m.Z);
            if (floor is { } f && m.Z <= f)
            {
                double drop = m.PeakZ - f;
                m.Z = f;
                m.Air = false;
                m.Vz = 0;
                if (drop > BreakHeight && !m.Steelie) { Lose("BROKEN!", Sfx.Break, m); return; }
                if (drop > 0.6)
                {
                    m.Vx *= 0.85;
                    m.Vy *= 0.85;
                    if (!m.Steelie) Play(Sfx.Land);
                }
            }
            else if (m.Z < course.Lowest - 7)
            {
                if (m.Steelie) { m.Gone = true; m.Away = 0; }
                else Lose("FELL OFF!", Sfx.Fall, m);
            }
        }
    }

    /// <summary>What the player's marble is rolling on: acid dissolves it, the goal ends the course.</summary>
    private void Ground(Marble m)
    {
        if (m.Air || Course.CellAt(m.X, m.Y) is not { } cell) return;
        switch (cell.Kind)
        {
            case CellKind.Acid:
                Effects.Add(new Effect { Kind = EffectKind.Bubbles, X = m.X, Y = m.Y, Z = m.Z, Life = 1.4 });
                Lose("DISSOLVED!", Sfx.Dissolve, m);
                break;
            case CellKind.Goal:
                Finish();
                break;
        }
    }

    /// <summary>Remembers the last place the marble was safely on solid, level-ish ground, away from edges and acid.</summary>
    private void RememberSafe(double dt)
    {
        _safeTimer -= dt;
        var m = Player;
        if (_safeTimer > 0 || m.Air) return;
        _safeTimer = 0.2;
        int cx = (int)Math.Floor(m.X), cy = (int)Math.Floor(m.Y);
        for (int dx = -1; dx <= 1; dx++)
        for (int dy = -1; dy <= 1; dy++)
        {
            if (!Course.Inside(cx + dx, cy + dy)) return;
            var n = Course.Cells[cx + dx, cy + dy];
            if (!n.Solid || n.Kind is CellKind.Acid or CellKind.Wave || Math.Abs(n.Top - m.Z) > 1.2) return;
        }
        _safe = (cx + 0.5, cy + 0.5, Course.Floor(cx + 0.5, cy + 0.5) ?? m.Z);
    }

    private void Lose(string why, Sfx sound, Marble m)
    {
        Mishap = why;
        Play(sound);
        if (sound == Sfx.Break) Effects.Add(new Effect { Kind = EffectKind.Shards, X = m.X, Y = m.Y, Z = m.Z, Life = 1.5 });
        Say(why, MishapSeconds);
        SetPhase(Phase.Mishap);
    }

    private void Respawn()
    {
        Player = new Marble { X = _safe.X, Y = _safe.Y, Z = _safe.Z };
        Mishap = null;
        SetPhase(Phase.Rolling);
        // Steelies that were hounding it go home.
        foreach (var s in Steelies.Where(s => Dist(s, Player) < 3)) { s.X = s.HomeX; s.Y = s.HomeY; s.Z = s.HomeZ; s.Vx = s.Vy = 0; }
    }

    private static double Dist(Marble a, Marble b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    // ───────────────────────────── the opposition ─────────────────────────────

    private void MoveSteelies(double dt)
    {
        var p = Player;
        foreach (var s in Steelies)
        {
            if (s.Gone)
            {
                s.Away += dt;
                if (s.Away > 5 && Math.Sqrt((p.X - s.HomeX) * (p.X - s.HomeX) + (p.Y - s.HomeY) * (p.Y - s.HomeY)) > 4)
                {
                    s.Gone = false;
                    s.X = s.HomeX; s.Y = s.HomeY; s.Z = s.HomeZ; s.Vx = s.Vy = s.Vz = 0; s.Air = false;
                }
                continue;
            }
            // Hunts the player on its own level; otherwise drifts home. Shies away from edges.
            Vec want;
            bool hunting = Phase == Phase.Rolling && Math.Abs(p.Z - s.Z) < 1.3 && Dist(s, p) < 7;
            if (hunting) want = new Vec(p.X - s.X, p.Y - s.Y);
            else want = new Vec(s.HomeX - s.X, s.HomeY - s.Y);
            double len = want.Length;
            var push = len < 0.3 ? default : want * ((hunting ? 6.5 : 4) / len);
            var ahead = new Vec(s.X + s.Vx * 0.25, s.Y + s.Vy * 0.25);
            if (Course.Floor(ahead.X, ahead.Y, Time) is not { } fa || fa < s.Z - 0.5) push = new Vec(-s.Vx, -s.Vy) * 3;
            for (int i = 0; i < 2; i++) Roll(s, push, dt / 2, brake: !hunting && len < 1.5);
        }
    }

    private void MoveGulpers(double dt)
    {
        foreach (var g in Gulpers)
        {
            g.Age += dt;
            if (g.Gulping > 0) { g.Gulping -= dt; continue; }
            double len = Math.Sqrt((g.BX - g.AX) * (g.BX - g.AX) + (g.BY - g.AY) * (g.BY - g.AY));
            g.T += g.Dir * g.Speed * 4 / Math.Max(1, len) * dt;
            if (g.T > 1) { g.T = 1; g.Dir = -1; }
            if (g.T < 0) { g.T = 0; g.Dir = 1; }
            g.X = g.AX + (g.BX - g.AX) * g.T;
            g.Y = g.AY + (g.BY - g.AY) * g.T;
            g.Z = Course.Floor(g.X, g.Y, Time) ?? 0;
        }
    }

    /// <summary>Marbles knock each other about; gulpers swallow the player's.</summary>
    private void Bumps()
    {
        var p = Player;
        foreach (var s in Steelies.Where(s => !s.Gone))
        {
            double d = Dist(s, p);
            if (d > Marble.Radius * 2 || d < 1e-6 || Math.Abs(s.Z - p.Z) > 0.6) continue;
            var n = new Vec((p.X - s.X) / d, (p.Y - s.Y) / d);
            double rel = (p.V - s.V).Dot(n);
            if (rel >= 0) continue;
            // The steelie is heavier: it gives less than it gets.
            double kp = 1.25, ks = 0.75;
            p.Vx -= kp * rel * n.X; p.Vy -= kp * rel * n.Y;
            s.Vx += ks * rel * n.X; s.Vy += ks * rel * n.Y;
            double push = (Marble.Radius * 2 - d) / 2;
            p.X += n.X * push; p.Y += n.Y * push;
            s.X -= n.X * push; s.Y -= n.Y * push;
            Play(Sfx.Clack);
        }
        foreach (var g in Gulpers)
        {
            if (g.Gulping > 0 || p.Air) continue;
            if (Math.Sqrt((g.X - p.X) * (g.X - p.X) + (g.Y - p.Y) * (g.Y - p.Y)) < 0.62 && Math.Abs(g.Z - p.Z) < 0.8)
            {
                g.Gulping = 2;
                Lose("GULPED!", Sfx.Gulp, p);
                return;
            }
        }
    }

    // ───────────────────────────── the end of a course ─────────────────────────────

    private void Finish()
    {
        Music(false);
        Play(Sfx.Goal);
        int seconds = (int)TimeLeft;
        FinishBonus = 1000 * (CourseIndex + 1) + seconds * 100;
        Score += FinishBonus;
        Carried = TimeLeft;
        Say("FINISHED!", 3);
        SetPhase(Phase.Finish);
    }

    private void NextCourse()
    {
        if (CourseIndex + 1 >= Courses.Count)
        {
            Score += 10000;
            Play(Sfx.Victory);
            SetPhase(Phase.Victory);
            return;
        }
        StartCourse(CourseIndex + 1);
    }

    // ───────────────────────────── test hooks ─────────────────────────────

    /// <summary>Test hook: skip the count down.</summary>
    internal void Go()
    {
        if (Phase != Phase.CourseStart) return;
        SetPhase(Phase.Rolling);
    }

    internal void Run(int ticks, Controls c)
    {
        for (int i = 0; i < ticks; i++) Tick(c);
    }

    internal void Idle(int ticks) => Run(ticks, default);

    internal void Place(double x, double y)
    {
        Player = new Marble { X = x, Y = y, Z = Course.Floor(x, y) ?? 0 };
        _safe = (x, y, Player.Z);
    }

    internal void UseCourse(Course c)
    {
        Course = c;
        Steelies.Clear();
        Gulpers.Clear();
        Place(c.StartX, c.StartY);
        SetPhase(Phase.Rolling);
    }

    internal void StartAt(int course) => StartCourse(course);

    internal Marble AddSteelie(double x, double y)
    {
        double z = Course.Floor(x, y) ?? 0;
        var s = new Marble { Steelie = true, X = x, Y = y, Z = z, HomeX = x, HomeY = y, HomeZ = z };
        Steelies.Add(s);
        return s;
    }

    internal Gulper AddGulper(double ax, double ay, double bx, double by)
    {
        var g = new Gulper { AX = ax, AY = ay, BX = bx, BY = by, X = ax, Y = ay, Z = Course.Floor(ax, ay) ?? 0 };
        Gulpers.Add(g);
        return g;
    }
}
