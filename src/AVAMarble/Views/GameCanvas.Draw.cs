using System.Globalization;
using Avalonia;
using Avalonia.Media;
using AVAMarble.Engine;

namespace AVAMarble.Views;

public sealed partial class GameCanvas
{
    private static readonly Dictionary<uint, IBrush> BrushCache = [];
    private static readonly Dictionary<(uint, double), Pen> PenCache = [];
    private static readonly Typeface Retro = new(new FontFamily("avares://AVAMarble/Assets/Fonts#VT323"));

    public static IBrush Brush(uint argb)
    {
        if (!BrushCache.TryGetValue(argb, out var b))
            BrushCache[argb] = b = new SolidColorBrush(Color.FromUInt32(argb)).ToImmutable();
        return b;
    }

    private static Pen Pen(uint argb, double width = 1)
    {
        if (!PenCache.TryGetValue((argb, width), out var p))
            PenCache[(argb, width)] = p = new Pen(Brush(argb), width);
        return p;
    }

    private const uint White = 0xFFF2F2F2, Yellow = 0xFFFFE14A, Red = 0xFFFF4A3A, Dim = 0xFF9AA2B8, Green = 0xFF70E070;

    private static void Text(DrawingContext dc, string text, Point at, double size, uint colour, bool center = false, bool right = false, bool shadow = true)
    {
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(colour));
        double x = center ? at.X - ft.Width / 2 : right ? at.X - ft.Width : at.X;
        if (shadow) dc.DrawText(new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(0xC0000000)), new Point(x + 2, at.Y - ft.Height / 2 + 2));
        dc.DrawText(ft, new Point(x, at.Y - ft.Height / 2));
    }

    private static uint Hash(int x, int y, int salt)
    {
        uint h = (uint)(x * 73856093) ^ (uint)(y * 19349663) ^ (uint)(salt * 83492791);
        h ^= h >> 13;
        h *= 0x5bd1e995;
        h ^= h >> 15;
        return h;
    }

    private static uint Shade(uint argb, double f)
    {
        uint r = (argb >> 16) & 0xFF, g = (argb >> 8) & 0xFF, b = argb & 0xFF;
        uint S(uint v) => (uint)Math.Clamp(f >= 1 ? v + (255 - v) * (f - 1) : v * f, 0, 255);
        return (argb & 0xFF000000) | (S(r) << 16) | (S(g) << 8) | S(b);
    }

    private static void Poly(DrawingContext dc, uint fill, uint? edge, double width, params Point[] pts)
    {
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(pts[0], true);
            foreach (var p in pts.Skip(1)) c.LineTo(p);
            c.EndFigure(true);
        }
        dc.DrawGeometry(fill == 0 ? null : Brush(fill), edge is { } e ? Pen(e, width) : null, g);
    }

    private double _camX, _camY;

    /// <summary>A point of the course on screen, with the camera on the marble.</summary>
    private Point P(double x, double y, double z) =>
        new(Field.Width / 2 + (x - y) * Field.SX - _camX, 420 + (x + y) * Field.SY - z * Field.SZ - _camY);

    private void DrawScreen(DrawingContext dc)
    {
        var g = Engine;
        var m = g.Player;
        // Follow the marble, but hold still while it falls into the void, so it drops away out of sight.
        bool falling = (m.Air && g.Course.Floor(m.X, m.Y) is null) || g.Phase == Phase.Mishap;
        if (!falling)
        {
            _camX = (m.X - m.Y) * Field.SX;
            _camY = (m.X + m.Y) * Field.SY - m.Z * Field.SZ;
        }
        DrawBackdrop(dc);
        DrawCourse(dc);
        foreach (var e in g.Effects) DrawEffect(dc, e);
        DrawHud(dc);
        switch (g.State)
        {
            case GameState.Title: DrawTitle(dc); break;
            case GameState.HighScores: DrawHighScores(dc); break;
            case GameState.EnterInitials: DrawInitials(dc); break;
            default: DrawMessages(dc); break;
        }
        if (g.State == GameState.Paused)
        {
            dc.FillRectangle(Brush(0x90000000), new Rect(0, 0, Field.Width, Field.Height));
            Text(dc, "PAUSED", new Point(Field.Width / 2, 340), 80, Yellow, center: true);
            Text(dc, "ANY KEY TO CARRY ON - ESC TO QUIT", new Point(Field.Width / 2, 400), 26, White, center: true);
        }
        if (g.State == GameState.GameOver)
            Text(dc, g.Phase == Phase.Victory ? "WELL ROLLED!" : "GAME OVER", new Point(Field.Width / 2, 330), 100, g.Phase == Phase.Victory ? Yellow : Red, center: true);
        if (g.State == GameState.Attract && g.Phase != Phase.CourseStart)
            Text(dc, "DEMONSTRATION - PRESS ENTER TO PLAY", new Point(Field.Width / 2, 96), 28, Yellow, center: true);
    }

    // ───────────────────────────── the course ─────────────────────────────

    private void DrawBackdrop(DrawingContext dc)
    {
        for (int i = 0; i < 24; i++)
        {
            double t = i / 23.0;
            uint c = 0xFF000000 | (uint)(10 + 30 * t) << 16 | (uint)(8 + 10 * t) << 8 | (uint)(36 + 40 * t);
            dc.FillRectangle(Brush(c), new Rect(0, Field.Top + i * 30, Field.Width, 31));
        }
        // A faint grid, drifting with the camera, far below.
        double ox = -(_camX * 0.3) % 72, oy = -(_camY * 0.3) % 36;
        for (double x = ox - 72; x < Field.Width + 72; x += 72)
            dc.DrawLine(Pen(0x18FFFFFF, 1), new Point(x, Field.Top), new Point(x + 720, Field.Height + 360));
        for (double x = ox - 72; x < Field.Width + 800; x += 72)
            dc.DrawLine(Pen(0x18FFFFFF, 1), new Point(x, Field.Top), new Point(x - 720, Field.Height + 360));
        _ = oy;
    }

    private void DrawCourse(DrawingContext dc)
    {
        var g = Engine;
        var course = g.Course;
        double baseZ = course.Lowest - 6;
        // Marbles and gulpers, each drawn just after the diagonal row of tiles it stands on.
        var things = new List<(int Row, Action Draw)>();
        // A broken, dissolved or gulped marble is gone; one that fell off is still falling.
        if (g.Phase != Phase.Mishap || g.Mishap == "FELL OFF!") things.Add((Row(g.Player), () => DrawMarble(dc, g.Player)));
        foreach (var s in g.Steelies.Where(s => !s.Gone)) things.Add((Row(s), () => DrawMarble(dc, s)));
        foreach (var gu in g.Gulpers) things.Add(((int)Math.Floor(gu.X) + (int)Math.Floor(gu.Y), () => DrawGulper(dc, gu)));
        // Marbles that have fallen below everything go first, behind the course.
        foreach (var t in things.Where(t => t.Row < 0)) t.Draw();
        int rows = course.W + course.H - 1;
        for (int s = 0; s < rows; s++)
        {
            for (int x = Math.Max(0, s - course.H + 1); x <= Math.Min(course.W - 1, s); x++)
            {
                int y = s - x;
                if (course.Cells[x, y].Solid) DrawTile(dc, course, x, y, baseZ);
            }
            foreach (var t in things.Where(t => t.Row == s)) t.Draw();
        }
        foreach (var t in things.Where(t => t.Row >= rows)) t.Draw();
        // The goal's banner.
        DrawGoalFlag(dc, course);
    }

    private int Row(Marble m)
    {
        // A marble that has fallen well below the course is drawn behind it.
        if (m.Z < Engine.Course.Lowest - 0.5 && Engine.Course.Floor(m.X, m.Y) is null) return -1;
        return (int)Math.Floor(m.X) + (int)Math.Floor(m.Y);
    }

    private double Corner(Course course, Cell c, double h, double x, double y) =>
        c.Kind == CellKind.Wave ? h + Course.Wave(x, y, Engine.Time) : h;

    private void DrawTile(DrawingContext dc, Course course, int x, int y, double baseZ)
    {
        var c = course.Cells[x, y];
        double h00 = Corner(course, c, c.H00, x, y), h10 = Corner(course, c, c.H10, x + 1, y);
        double h01 = Corner(course, c, c.H01, x, y + 1), h11 = Corner(course, c, c.H11, x + 1, y + 1);
        var p00 = P(x, y, h00);
        var p10 = P(x + 1, y, h10);
        var p01 = P(x, y + 1, h01);
        var p11 = P(x + 1, y + 1, h11);
        // Cull tiles well off screen.
        double minX = Math.Min(Math.Min(p00.X, p10.X), Math.Min(p01.X, p11.X)), maxX = Math.Max(Math.Max(p00.X, p10.X), Math.Max(p01.X, p11.X));
        if (maxX < -40 || minX > Field.Width + 40 || p00.Y > Field.Height + 40) return;
        // The walls: the east face and the south face, down to the next tile or into the void.
        double e0 = baseZ, e1 = baseZ;
        if (course.Inside(x + 1, y) && course.Cells[x + 1, y].Solid) { var n = course.Cells[x + 1, y]; e0 = Corner(course, n, n.H00, x + 1, y); e1 = Corner(course, n, n.H01, x + 1, y + 1); }
        if (h10 > e0 + 0.01 || h11 > e1 + 0.01)
            DrawWall(dc, p10, p11, P(x + 1, y + 1, Math.Min(e1, h11)), P(x + 1, y, Math.Min(e0, h10)), Shade(course.Colour, 0.55));
        double s0 = baseZ, s1 = baseZ;
        if (course.Inside(x, y + 1) && course.Cells[x, y + 1].Solid) { var n = course.Cells[x, y + 1]; s0 = Corner(course, n, n.H00, x, y + 1); s1 = Corner(course, n, n.H10, x + 1, y + 1); }
        if (h01 > s0 + 0.01 || h11 > s1 + 0.01)
            DrawWall(dc, p01, p11, P(x + 1, y + 1, Math.Min(s1, h11)), P(x, y + 1, Math.Min(s0, h01)), Shade(course.Colour, 0.75));
        // The top, lit from the upper left.
        double gx = (h10 - h00 + h11 - h01) / 2, gy = (h01 - h00 + h11 - h10) / 2;
        double light = Math.Clamp(1 + 0.28 * gx - 0.12 * gy, 0.55, 1.25);
        uint top = c.Kind switch
        {
            CellKind.Acid => 0xFF4AC83A,
            CellKind.Wave => 0xFF4A7AD0,
            _ => course.Colour,
        };
        if ((x + y) % 2 == 0 && c.Kind is CellKind.Floor or CellKind.Wave) top = Shade(top, 0.93);
        Poly(dc, Shade(top, light), Shade(top, light * 0.75), 1, p00, p10, p11, p01);
        switch (c.Kind)
        {
            case CellKind.Goal:
                // A chequered finish.
                for (int i = 0; i < 2; i++)
                for (int j = 0; j < 2; j++)
                {
                    if ((i + j) % 2 == 1) continue;
                    double ax = x + i * 0.5, ay = y + j * 0.5;
                    Poly(dc, 0xFF141414, null, 0, P(ax, ay, h00), P(ax + 0.5, ay, h00), P(ax + 0.5, ay + 0.5, h00), P(ax, ay + 0.5, h00));
                }
                break;
            case CellKind.Acid:
                for (int k = 0; k < 2; k++)
                {
                    uint hsh = Hash(x, y, k);
                    double phase = (Engine.Time * 0.8 + (hsh % 100) / 100.0) % 1;
                    var at = P(x + 0.25 + (hsh % 50) / 100.0, y + 0.25 + ((hsh >> 8) % 50) / 100.0, h00);
                    dc.DrawEllipse(null, Pen(0xC0B0FF90, 1.5), at, 3 + phase * 6, (3 + phase * 6) * 0.5);
                }
                break;
            case CellKind.Wave:
                dc.DrawLine(Pen(0x60FFFFFF, 1.5), P(x + 0.1, y + 0.5, (h00 + h11) / 2 + 0.02), P(x + 0.9, y + 0.5, (h00 + h11) / 2 + 0.02));
                break;
        }
    }

    private static void DrawWall(DrawingContext dc, Point a, Point b, Point c, Point d, uint colour)
    {
        Poly(dc, colour, Shade(colour, 0.7), 1, a, b, c, d);
    }

    private void DrawGoalFlag(DrawingContext dc, Course course)
    {
        // Over the middle of the goal tiles.
        double sx = 0, sy = 0, sz = 0;
        int n = 0;
        for (int x = 0; x < course.W; x++)
        for (int y = 0; y < course.H; y++)
            if (course.Cells[x, y].Solid && course.Cells[x, y].Kind == CellKind.Goal) { sx += x + 0.5; sy += y + 0.5; sz = course.Cells[x, y].H00; n++; }
        if (n == 0) return;
        var foot = P(sx / n, sy / n, sz);
        dc.DrawLine(Pen(0xFFE0E0E0, 3), foot, new Point(foot.X, foot.Y - 90));
        double wave = Math.Sin(Engine.Time * 5) * 4;
        Poly(dc, 0xFFF0F0F0, 0xFF101010, 1.5, new Point(foot.X, foot.Y - 90), new Point(foot.X + 70, foot.Y - 82 + wave), new Point(foot.X + 70, foot.Y - 52 + wave), new Point(foot.X, foot.Y - 60));
        Text(dc, "GOAL", new Point(foot.X + 35, foot.Y - 72 + wave / 2), 24, 0xFFC02020, center: true, shadow: false);
    }

    // ───────────────────────────── marbles and gulpers ─────────────────────────────

    private void DrawMarble(DrawingContext dc, Marble m)
    {
        var course = Engine.Course;
        double r = Marble.Radius * 40;
        if (course.Floor(m.X, m.Y, Engine.Time) is { } floor && m.Air && floor < m.Z)
        {
            var sh = P(m.X, m.Y, floor);
            double k = Math.Clamp(1 - (m.Z - floor) / 6, 0.3, 1);
            dc.DrawEllipse(Brush(0x60000000), null, sh, r * k, r * 0.5 * k);
        }
        else if (!m.Air)
            dc.DrawEllipse(Brush(0x50000000), null, P(m.X + 0.1, m.Y + 0.1, m.Z), r * 0.9, r * 0.45);
        var c = P(m.X, m.Y, m.Z);
        c = new Point(c.X, c.Y - r);
        uint body = m.Steelie ? 0xFF2A2A30 : 0xFF2A6AE8;
        uint dark = m.Steelie ? 0xFF0C0C10 : 0xFF143A90;
        dc.DrawEllipse(Brush(dark), null, c, r, r);
        dc.DrawEllipse(Brush(body), null, new Point(c.X - r * 0.12, c.Y - r * 0.12), r * 0.86, r * 0.86);
        // A band that turns as it rolls.
        double a = (m.RollX - m.RollY) * 1.6, b = (m.RollX + m.RollY) * 1.6;
        double bx = Math.Sin(a) * r * 0.7, by = Math.Sin(b) * r * 0.5;
        dc.DrawEllipse(null, Pen(m.Steelie ? 0x60A0A0B0 : 0x90FFFFFF, 2), new Point(c.X + bx * 0.3, c.Y + by * 0.3), r * 0.55 * Math.Abs(Math.Cos(a)) + 2, r * 0.55 * Math.Abs(Math.Cos(b)) + 2);
        dc.DrawEllipse(Brush(m.Steelie ? 0x90E0E0F0 : 0xD0FFFFFF), null, new Point(c.X - r * 0.38, c.Y - r * 0.4), r * 0.26, r * 0.2);
    }

    private void DrawGulper(DrawingContext dc, Gulper g)
    {
        var foot = P(g.X, g.Y, g.Z);
        double hop = Math.Abs(Math.Sin(g.Age * 7)) * 10;
        if (g.Gulping > 0) hop = 0;
        // A green coil, stretched as it hops.
        for (int i = 0; i < 6; i++)
        {
            double y = foot.Y - 6 - i * (5 + hop * 0.25);
            dc.DrawEllipse(Brush(i % 2 == 0 ? 0xFF3AB03A : 0xFF2A8A2A), Pen(0xFF145014, 1.2), new Point(foot.X, y), 13, 5);
        }
        double top = foot.Y - 6 - 6 * (5 + hop * 0.25);
        dc.DrawEllipse(Brush(0xFF4AC84A), Pen(0xFF145014, 1.5), new Point(foot.X, top), 15, 11);
        dc.DrawEllipse(Brush(White), null, new Point(foot.X - 5, top - 4), 4, 4);
        dc.DrawEllipse(Brush(White), null, new Point(foot.X + 5, top - 4), 4, 4);
        dc.DrawEllipse(Brush(0xFF101010), null, new Point(foot.X - 5, top - 3), 2, 2);
        dc.DrawEllipse(Brush(0xFF101010), null, new Point(foot.X + 5, top - 3), 2, 2);
        double mouth = g.Gulping > 0 ? 6 : 2 + Math.Abs(Math.Sin(g.Age * 3)) * 2;
        dc.DrawEllipse(Brush(0xFF601010), null, new Point(foot.X, top + 5), 6, mouth);
    }

    // ───────────────────────────── effects and words ─────────────────────────────

    private void DrawEffect(DrawingContext dc, Engine.Effect e)
    {
        double t = e.Age / e.Life;
        switch (e.Kind)
        {
            case EffectKind.Score:
            case EffectKind.Word:
                Text(dc, e.Text, new Point(P(e.X, e.Y, e.Z).X, P(e.X, e.Y, e.Z).Y - 40 - t * 40), 28, e.Colour, center: true);
                break;
            case EffectKind.Shards:
            {
                var c = P(e.X, e.Y, e.Z);
                for (int i = 0; i < 14; i++)
                {
                    double ang = i * 0.45, sp = 30 + Hash(i, 1, 3) % 60;
                    double x = c.X + Math.Cos(ang) * sp * t * 2, y = c.Y - 14 + Math.Sin(ang) * sp * t - 80 * t + 200 * t * t;
                    dc.FillRectangle(Brush(i % 2 == 0 ? 0xD02A6AE8 : 0xD0B0D0FF), new Rect(x, y, 5, 4));
                }
                break;
            }
            case EffectKind.Bubbles:
            {
                var c = P(e.X, e.Y, e.Z);
                for (int i = 0; i < 9; i++)
                {
                    double x = c.X + (Hash(i, 2, 5) % 30) - 15, y = c.Y - (Hash(i, 3, 5) % 20) - t * 30;
                    dc.DrawEllipse(null, Pen(0xC0B0FF90, 2), new Point(x, y), 3 + t * 4, 3 + t * 4);
                }
                dc.DrawEllipse(Brush((uint)((1 - t) * 200) << 24 | 0x002A6AE8), null, new Point(c.X, c.Y - 10 * (1 - t)), 13 * (1 - t), 13 * (1 - t));
                break;
            }
        }
    }

    private void DrawHud(DrawingContext dc)
    {
        var g = Engine;
        dc.FillRectangle(Brush(0xFF0C0A18), new Rect(0, 0, Field.Width, Field.Top));
        dc.FillRectangle(Brush(0xFF4A5AA0), new Rect(0, Field.Top - 3, Field.Width, 3));
        Text(dc, "SCORE", new Point(14, 16), 22, Dim);
        Text(dc, g.Score.ToString("D6"), new Point(14, 42), 36, White);
        Text(dc, $"COURSE {g.CourseIndex + 1} OF {Courses.Count}", new Point(250, 16), 22, Dim);
        Text(dc, g.Course.Name, new Point(250, 42), 30, Yellow);
        int secs = (int)Math.Ceiling(g.TimeLeft);
        bool low = secs <= 10 && g.State is GameState.Playing or GameState.Attract;
        Text(dc, "TIME", new Point(Field.Width - 150, 16), 22, Dim);
        if (!low || (int)(_clock * 4) % 2 == 0) Text(dc, secs.ToString(), new Point(Field.Width - 150, 42), 44, low ? Red : White);
        Text(dc, "HI", new Point(Field.Width - 300, 16), 22, Dim);
        Text(dc, Math.Max(g.HighScores.TopScore, g.Score).ToString("D6"), new Point(Field.Width - 300, 42), 28, Dim);
    }

    private void DrawMessages(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        switch (g.Phase)
        {
            case Phase.CourseStart when g.State is GameState.Playing or GameState.Attract or GameState.Paused:
            {
                dc.FillRectangle(Brush(0xC00C0A18), new Rect(170, 200, 684, 260));
                dc.DrawRectangle(Pen(0xFF4A5AA0, 3), new Rect(170, 200, 684, 260));
                Text(dc, $"COURSE {g.CourseIndex + 1}", new Point(cx, 236), 30, Dim, center: true);
                Text(dc, g.Course.Name, new Point(cx, 290), 64, Yellow, center: true);
                Text(dc, g.Course.Note, new Point(cx, 342), 28, White, center: true);
                string time = g.Carried >= 1 ? $"TIME {g.Course.Time:0} + {g.Carried:0} CARRIED OVER" : $"TIME {g.Course.Time:0} SECONDS";
                Text(dc, time, new Point(cx, 382), 28, Green, center: true);
                int count = Math.Max(1, 3 - (int)g.PhaseTime);
                if (g.PhaseTime > 0.5) Text(dc, count.ToString(), new Point(cx, 430), 44, Yellow, center: true);
                return;
            }
            case Phase.Finish:
                dc.FillRectangle(Brush(0xC00C0A18), new Rect(220, 220, 584, 200));
                Text(dc, "FINISHED!", new Point(cx, 262), 64, Yellow, center: true);
                Text(dc, $"COURSE BONUS {1000 * (g.CourseIndex + 1)}", new Point(cx, 322), 30, White, center: true);
                Text(dc, $"TIME BONUS {(int)g.Carried} x 100", new Point(cx, 356), 30, White, center: true);
                Text(dc, g.CourseIndex + 1 < Courses.Count ? $"{(int)g.Carried} SECONDS CARRY OVER" : "THAT WAS THE LAST ONE", new Point(cx, 392), 26, Green, center: true);
                return;
            case Phase.Victory:
                Text(dc, "ALL SIX COURSES!", new Point(cx, 250), 72, Yellow, center: true);
                Text(dc, "BONUS 10000", new Point(cx, 316), 40, White, center: true);
                return;
        }
        if (g.Message is { } msg) Text(dc, msg, new Point(cx, 180), 66, g.Phase == Phase.Mishap || g.Phase == Phase.TimeUp ? Red : Yellow, center: true);
    }

    private void DrawTitle(DrawingContext dc)
    {
        double cx = Field.Width / 2;
        dc.FillRectangle(Brush(0xB00C0A18), new Rect(0, Field.Top, Field.Width, Field.Height - Field.Top));
        Text(dc, "AVA MARBLE", new Point(cx, 170), 130, 0xFF6A9AF8, center: true);
        Text(dc, "AFTER THE 1984 ARCADE CLASSIC", new Point(cx, 244), 28, Dim, center: true);
        // A marble rolling across the title.
        double mx = cx + Math.Sin(_clock * 0.9) * 300;
        var fake = new Marble { X = 0, Y = 0, Z = 0, RollX = _clock * 2, RollY = -_clock };
        var save = (_camX, _camY);
        _camX = -(mx - Field.Width / 2);
        _camY = -(300 - 420);
        DrawMarble(dc, fake);
        (_camX, _camY) = save;
        string[] lines =
        [
            "ROLL YOUR MARBLE DOWN SIX COURSES TO THE GOAL",
            "BEFORE THE CLOCK RUNS OUT - SPARE TIME CARRIES OVER",
            "DON'T FALL OFF, OR DROP TOO FAR AND BREAK",
            "BEWARE STEELIES, GULPERS, ACID AND WAVES",
        ];
        for (int i = 0; i < lines.Length; i++) Text(dc, lines[i], new Point(cx, 380 + i * 38), 30, White, center: true);
        if ((int)(_clock * 2) % 2 == 0) Text(dc, "PRESS ENTER OR START", new Point(cx, 590), 48, Yellow, center: true);
        Text(dc, "ARROWS: PUSH THE MARBLE    Z / SPACE: BRAKE", new Point(cx, 670), 28, Dim, center: true);
    }

    private void DrawHighScores(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        dc.FillRectangle(Brush(0xD00C0A18), new Rect(0, Field.Top, Field.Width, Field.Height - Field.Top));
        Text(dc, "BEST ROLLERS", new Point(cx, 130), 72, Yellow, center: true);
        Text(dc, "RANK NAME    SCORE   COURSE", new Point(cx, 196), 30, Dim, center: true);
        var entries = g.HighScores.Entries;
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            bool mine = i == g.NewEntryIndex && (int)(_clock * 4) % 2 == 0;
            Text(dc, $"{i + 1,2}.  {e.Initials}  {e.Score,8}   {e.Stage,3}", new Point(cx, 240 + i * 42), 34, mine ? Red : i == 0 ? Yellow : White, center: true);
        }
        Text(dc, "PRESS ENTER TO PLAY", new Point(cx, 700), 30, White, center: true);
    }

    private void DrawInitials(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        dc.FillRectangle(Brush(0xD00C0A18), new Rect(0, Field.Top, Field.Width, Field.Height - Field.Top));
        Text(dc, "A NEW HIGH SCORE!", new Point(cx, 200), 64, Yellow, center: true);
        Text(dc, g.Score.ToString(), new Point(cx, 270), 52, White, center: true);
        Text(dc, "ENTER YOUR INITIALS", new Point(cx, 350), 34, Dim, center: true);
        if (g.Initials is { } entry)
        {
            string s = entry.Initials;
            for (int i = 0; i < s.Length; i++)
            {
                bool cur = i == entry.Cursor && (int)(_clock * 3) % 2 == 0;
                Text(dc, s[i].ToString(), new Point(cx - 60 + i * 60, 440), 80, cur ? Red : Yellow, center: true);
                dc.FillRectangle(Brush(i == entry.Cursor ? Red : Dim), new Rect(cx - 82 + i * 60, 480, 44, 4));
            }
        }
        Text(dc, "TYPE, OR UP/DOWN AND SPACE; ENTER WHEN DONE", new Point(cx, 560), 26, White, center: true);
    }
}
