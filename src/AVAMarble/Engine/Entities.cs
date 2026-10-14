namespace AVAMarble.Engine;

public readonly record struct Vec(double X, double Y)
{
    public static Vec operator +(Vec a, Vec b) => new(a.X + b.X, a.Y + b.Y);
    public static Vec operator -(Vec a, Vec b) => new(a.X - b.X, a.Y - b.Y);
    public static Vec operator *(Vec a, double k) => new(a.X * k, a.Y * k);
    public double Length => Math.Sqrt(X * X + Y * Y);
    public double Dot(Vec o) => X * o.X + Y * o.Y;
}

/// <summary>The stick pushes the marble across the screen (up is up the screen); the button brakes.</summary>
public readonly record struct Controls(int Dx, int Dy, bool Brake = false);

/// <summary>
/// The course is a grid of tiles seen from above at an angle: X runs down to the right on screen,
/// Y down to the left, and Z is height. A tile is one unit square.
/// </summary>
public static class Field
{
    public const double Step = 1.0 / 60;
    public const double Width = 1024, Height = 768;
    /// <summary>The bottom of the clock bar.</summary>
    public const double Top = 64;
    /// <summary>Screen pixels per unit: across, down, and up.</summary>
    public const double SX = 36, SY = 18, SZ = 18;

    /// <summary>The world direction that is "right" on screen, and "down".</summary>
    public static readonly Vec ScreenRight = new(Math.Sqrt(0.5), -Math.Sqrt(0.5));
    public static readonly Vec ScreenDown = new(Math.Sqrt(0.5), Math.Sqrt(0.5));
}

public sealed class Marble
{
    public const double Radius = 0.34;
    public double X, Y, Z, Vx, Vy, Vz;
    public bool Air;
    /// <summary>The highest point of the current fall, to judge whether the landing breaks it.</summary>
    public double PeakZ;
    /// <summary>A black steelie, hunting the player, rather than the player's own marble.</summary>
    public bool Steelie;
    public double HomeX, HomeY, HomeZ;
    public bool Gone;
    public double Away;
    /// <summary>The marble's spin, for the highlight that turns as it rolls.</summary>
    public double RollX, RollY;
    public Vec V => new(Vx, Vy);
    public double Speed => V.Length;
}

/// <summary>A green gulper: it hops to and fro between two points and swallows any marble it meets.</summary>
public sealed class Gulper
{
    public double AX, AY, BX, BY;
    public double T, Dir = 1, Speed = 0.35;
    public double X, Y, Z, Age;
    public double Gulping;
}

public enum EffectKind { Score, Word, Shards, Bubbles, Dust }

public sealed class Effect
{
    public EffectKind Kind;
    public double X, Y, Z, Age, Life = 1;
    public string Text = "";
    public uint Colour = 0xFFFFFFFF;
    public bool Done => Age >= Life;
}
