namespace AVAMarble.Engine;

public enum CellKind { Floor, Goal, Acid, Wave }

/// <summary>One tile: whether it's there, its height at each corner, and what sort of floor it is.</summary>
public struct Cell
{
    public bool Solid;
    /// <summary>Heights at (x, y), (x+1, y), (x, y+1) and (x+1, y+1).</summary>
    public double H00, H10, H01, H11;
    public CellKind Kind;
    public double Top => Math.Max(Math.Max(H00, H10), Math.Max(H01, H11));
    public double Low => Math.Min(Math.Min(H00, H10), Math.Min(H01, H11));
}

/// <summary>A point on the way to the goal, and how fast to take it (for the computer's marble).</summary>
public readonly record struct Waypoint(double X, double Y, double Speed = 4.5);

/// <summary>
/// A course: its tiles, the start, the way to the goal, its time, and who lies in wait. Courses are
/// built from flats and ramps; neighbouring tiles at different heights make a ledge, which a marble
/// drops off (and bounces off from below).
/// </summary>
public sealed class Course
{
    public string Name = "";
    public string Note = "";
    public int W, H;
    public Cell[,] Cells = new Cell[1, 1];
    public double Time;
    public double StartX, StartY;
    public List<Waypoint> Path = [];
    public List<(double X, double Y)> Steelies = [];
    public List<(double AX, double AY, double BX, double BY)> Gulpers = [];
    public uint Colour = 0xFF8A8AA0;

    public Course(int w, int h)
    {
        W = w;
        H = h;
        Cells = new Cell[w, h];
    }

    public bool Inside(int x, int y) => x >= 0 && y >= 0 && x < W && y < H;

    public Cell? CellAt(double x, double y)
    {
        int cx = (int)Math.Floor(x), cy = (int)Math.Floor(y);
        if (!Inside(cx, cy) || !Cells[cx, cy].Solid) return null;
        return Cells[cx, cy];
    }

    /// <summary>How high the floor is at a point, or null over thin air. Wave floors ripple with time.</summary>
    public double? Floor(double x, double y, double time = 0)
    {
        int cx = (int)Math.Floor(x), cy = (int)Math.Floor(y);
        if (!Inside(cx, cy)) return null;
        var c = Cells[cx, cy];
        if (!c.Solid) return null;
        double fx = x - cx, fy = y - cy;
        double h = c.H00 * (1 - fx) * (1 - fy) + c.H10 * fx * (1 - fy) + c.H01 * (1 - fx) * fy + c.H11 * fx * fy;
        if (c.Kind == CellKind.Wave) h += Wave(x, y, time);
        return h;
    }

    public static double Wave(double x, double y, double time) => 0.4 * Math.Sin(time * 2.6 - (x + y) * 1.1);

    /// <summary>Which way and how steeply the floor slopes at a point (uphill).</summary>
    public Vec Slope(double x, double y, double time = 0)
    {
        int cx = (int)Math.Floor(x), cy = (int)Math.Floor(y);
        if (!Inside(cx, cy) || !Cells[cx, cy].Solid) return default;
        var c = Cells[cx, cy];
        double fx = x - cx, fy = y - cy;
        double gx = (c.H10 - c.H00) * (1 - fy) + (c.H11 - c.H01) * fy;
        double gy = (c.H01 - c.H00) * (1 - fx) + (c.H11 - c.H10) * fx;
        if (c.Kind == CellKind.Wave)
        {
            double d = -0.4 * 1.1 * Math.Cos(time * 2.6 - (x + y) * 1.1);
            gx += d;
            gy += d;
        }
        return new Vec(gx, gy);
    }

    public double Lowest
    {
        get
        {
            double low = double.MaxValue;
            foreach (var c in Cells) if (c.Solid) low = Math.Min(low, c.Low);
            return low == double.MaxValue ? 0 : low;
        }
    }

    // ───────────────────────────── building ─────────────────────────────

    public Course Flat(int x0, int y0, int w, int h, double z, CellKind kind = CellKind.Floor)
    {
        for (int x = x0; x < x0 + w; x++)
        for (int y = y0; y < y0 + h; y++)
            Cells[x, y] = new Cell { Solid = true, H00 = z, H10 = z, H01 = z, H11 = z, Kind = kind };
        return this;
    }

    /// <summary>A slope from <paramref name="from"/> to <paramref name="to"/> along X ('x') or Y ('y') across the region.</summary>
    public Course Ramp(int x0, int y0, int w, int h, char axis, double from, double to)
    {
        int n = axis == 'x' ? w : h;
        for (int x = x0; x < x0 + w; x++)
        for (int y = y0; y < y0 + h; y++)
        {
            int i = axis == 'x' ? x - x0 : y - y0;
            double a = from + (to - from) * i / n, b = from + (to - from) * (i + 1) / n;
            Cells[x, y] = axis == 'x'
                ? new Cell { Solid = true, H00 = a, H01 = a, H10 = b, H11 = b }
                : new Cell { Solid = true, H00 = a, H10 = a, H01 = b, H11 = b };
        }
        return this;
    }

    public Course Mark(int x0, int y0, int w, int h, CellKind kind)
    {
        for (int x = x0; x < x0 + w; x++)
        for (int y = y0; y < y0 + h; y++)
            if (Cells[x, y].Solid) Cells[x, y].Kind = kind;
        return this;
    }

    public Course Hole(int x0, int y0, int w, int h)
    {
        for (int x = x0; x < x0 + w; x++)
        for (int y = y0; y < y0 + h; y++)
            Cells[x, y].Solid = false;
        return this;
    }
}

/// <summary>The six courses, in order. Their names and layouts are this game's own.</summary>
public static class Courses
{
    public static int Count => 6;

    public static Course Make(int index) => index switch
    {
        0 => WarmUp(),
        1 => Nursery(),
        2 => AcidFlats(),
        3 => SkyWalk(),
        4 => Topsy(),
        _ => Gauntlet(),
    };

    private static Course WarmUp()
    {
        var c = new Course(24, 34) { Name = "WARM-UP RUN", Note = "ROLL DOWN TO THE GOAL", Time = 40, StartX = 4, StartY = 4, Colour = 0xFF8C9AB8 };
        c.Flat(1, 1, 6, 6, 16)
         .Ramp(1, 7, 6, 7, 'y', 16, 12)
         .Flat(1, 14, 10, 5, 12)
         .Ramp(11, 14, 6, 5, 'x', 12, 9)
         .Flat(17, 14, 5, 5, 9)
         .Ramp(17, 19, 5, 8, 'y', 9, 5)
         .Flat(17, 27, 5, 4, 5, CellKind.Goal);
        c.Path = [new(4, 4), new(4, 10, 5), new(4, 16.5), new(9, 16.5), new(15, 16.5, 5), new(19.5, 16.5, 3.5), new(19.5, 22, 5), new(19.5, 28.5)];
        return c;
    }

    private static Course Nursery()
    {
        var c = new Course(26, 40) { Name = "THE NURSERY SLOPES", Note = "MIND THE STEELIE", Time = 40, StartX = 3.5, StartY = 3.5, Colour = 0xFF9AB08C };
        c.Flat(1, 1, 5, 5, 20)
         .Ramp(2, 6, 3, 8, 'y', 20, 15)
         .Flat(1, 14, 6, 4, 14)
         .Ramp(7, 15, 8, 3, 'x', 14, 10)
         .Flat(15, 13, 6, 7, 10)
         .Flat(16, 20, 4, 6, 8.5)
         .Ramp(16, 26, 4, 8, 'y', 8.5, 5)
         .Flat(14, 34, 8, 4, 5, CellKind.Goal);
        c.Steelies = [(19, 15)];
        c.Path = [new(3.5, 3.5), new(3.5, 10, 4), new(3.5, 15.5, 3), new(6.5, 16, 3.5), new(12, 16, 4), new(16, 16, 3), new(18, 18.5, 3), new(18, 23, 4), new(18, 30, 5), new(18, 35.5)];
        return c;
    }

    private static Course AcidFlats()
    {
        var c = new Course(30, 40) { Name = "ACID FLATS", Note = "DON'T TOUCH THE GREEN", Time = 50, StartX = 4.5, StartY = 3.5, Colour = 0xFFB0A07A };
        c.Flat(2, 1, 5, 5, 18)
         .Ramp(2, 6, 5, 6, 'y', 18, 13)
         .Flat(1, 12, 16, 10, 13)
         .Mark(7, 12, 2, 6, CellKind.Acid)
         .Mark(11, 16, 2, 6, CellKind.Acid)
         .Ramp(17, 18, 6, 4, 'x', 13, 10)
         .Flat(23, 16, 4, 10, 10, CellKind.Wave)
         .Ramp(23, 26, 4, 8, 'y', 10, 6)
         .Flat(21, 34, 7, 4, 6, CellKind.Goal);
        c.Gulpers = [(14.5, 13.5, 14.5, 20.5)];
        c.Path = [new(4.5, 3.5), new(4.5, 9, 4), new(4.5, 14, 3), new(4.5, 19.5, 3), new(9.8, 19.5, 2.5), new(9.8, 14, 2.5), new(13.5, 14, 2.5), new(15.8, 19.5, 2.5), new(19, 19.8, 3), new(24.8, 19.8, 2.5), new(24.8, 26, 3), new(24.8, 30, 4), new(24.8, 35.5)];
        return c;
    }

    private static Course SkyWalk()
    {
        var c = new Course(33, 40) { Name = "THE SKY WALK", Note = "NARROW, AND A LONG WAY DOWN", Time = 45, StartX = 4.5, StartY = 4.5, Colour = 0xFF8CB0C8 };
        c.Flat(2, 2, 5, 5, 24)
         .Ramp(3, 7, 2, 10, 'y', 24, 19)
         .Flat(1, 17, 6, 5, 19)
         .Flat(7, 18, 10, 2, 19)
         .Flat(17, 16, 6, 6, 17)
         .Ramp(19, 22, 2, 10, 'y', 17, 12)
         .Flat(16, 32, 8, 5, 12)
         .Ramp(24, 33, 4, 2, 'x', 12, 10)
         .Flat(28, 31, 4, 6, 10, CellKind.Goal);
        c.Steelies = [(2.5, 20.5), (21.5, 35)];
        c.Path = [new(4.5, 4.5), new(4, 7.5, 2.5), new(4, 16, 3), new(4, 19, 2.5), new(9, 19, 3), new(15, 19, 3), new(18.5, 19, 2.5), new(20, 20.5, 2.5), new(20, 24, 3), new(20, 31, 3), new(20, 34, 2.5), new(23.5, 34, 2.5), new(27, 34, 3), new(30, 34)];
        return c;
    }

    private static Course Topsy()
    {
        var c = new Course(26, 42) { Name = "TOPSY HILL", Note = "WHAT GOES DOWN MUST GO UP", Time = 40, StartX = 4.5, StartY = 4.5, Colour = 0xFFC0A0C0 };
        c.Flat(2, 2, 5, 5, 22)
         .Ramp(2, 7, 5, 8, 'y', 22, 12)
         .Flat(2, 15, 5, 3, 12)
         .Ramp(2, 18, 5, 5, 'y', 12, 14.5)
         .Flat(2, 23, 8, 4, 14.5)
         .Ramp(10, 23, 8, 4, 'x', 14.5, 11)
         .Flat(18, 22, 6, 6, 11)
         .Ramp(19, 28, 4, 8, 'y', 11, 7)
         .Flat(17, 36, 8, 4, 7, CellKind.Goal);
        c.Gulpers = [(18.5, 23.5, 23.5, 23.5), (23.5, 26.5, 18.5, 26.5)];
        c.Path = [new(4.5, 4.5), new(4.5, 10, 6), new(4.5, 16, 7), new(4.5, 21, 5), new(4.5, 25, 3), new(9, 25, 3.5), new(14, 25, 4), new(19.5, 25, 3), new(21, 27.5, 3), new(21, 31, 4), new(21, 37.5)];
        return c;
    }

    private static Course Gauntlet()
    {
        var c = new Course(26, 48) { Name = "THE GAUNTLET", Note = "EVERYTHING AT ONCE", Time = 50, StartX = 3.5, StartY = 3.5, Colour = 0xFFC88A7A };
        c.Flat(2, 2, 4, 4, 26)
         .Ramp(2, 6, 3, 8, 'y', 26, 21)
         .Flat(1, 14, 6, 4, 21)
         .Flat(7, 15, 8, 2, 21)
         .Flat(15, 13, 7, 7, 19)
         .Mark(17, 13, 2, 3, CellKind.Acid)
         .Ramp(17, 20, 3, 6, 'y', 19, 15)
         .Flat(16, 26, 6, 5, 15, CellKind.Wave)
         .Flat(16, 31, 6, 4, 13.5)
         .Ramp(18, 35, 2, 8, 'y', 13.5, 9)
         .Flat(16, 43, 7, 4, 9, CellKind.Goal);
        c.Steelies = [(2.5, 16.5), (20.5, 18.5)];
        c.Gulpers = [(16.5, 32.5, 21.5, 32.5)];
        c.Path = [new(3.5, 3.5), new(3.5, 9, 3), new(3.5, 15.5, 2.5), new(7.5, 16, 2.5), new(14, 16, 3), new(16, 16, 2.5), new(16.2, 18.5, 2.5), new(18.5, 19.5, 2.5), new(18.5, 25, 3), new(18.8, 28, 2.5), new(18.8, 33, 2.5), new(19, 35.5, 2.5), new(19, 42, 3), new(19, 45)];
        return c;
    }
}
