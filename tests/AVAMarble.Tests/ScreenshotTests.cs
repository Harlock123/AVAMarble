using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using AVAMarble.Engine;
using AVAMarble.Views;

namespace AVAMarble.Tests;

/// <summary>
/// Draws the game in a real (headless) window at the moments worth showing. Set MARBLE_SHOTS to a
/// folder to save them as PNGs: the README's screenshots come from here.
/// </summary>
public sealed class ScreenshotTests
{
    private static (Window W, GameCanvas Canvas, GameEngine G) Open(int seed = 11)
    {
        var g = new GameEngine(seed, HighScoreTable.Load(Path.Combine(Path.GetTempPath(), "avamarble-ui-" + Guid.NewGuid().ToString("N")))) { Silent = true };
        var canvas = new GameCanvas(g, new SoundManager(silent: true), startTimer: false);
        var w = new Window { Width = 1024, Height = 768, Content = canvas };
        w.Show();
        Pump();
        return (w, canvas, g);
    }

    private static void Pump()
    {
        for (int i = 0; i < 10; i++) Dispatcher.UIThread.RunJobs();
    }

    private static void Shot(Window w, GameCanvas c, string name)
    {
        c.InvalidateVisual();
        Pump();
        var frame = w.CaptureRenderedFrame();
        Assert.NotNull(frame);
        var dir = Environment.GetEnvironmentVariable("MARBLE_SHOTS");
        if (string.IsNullOrEmpty(dir)) return;
        Directory.CreateDirectory(dir);
        frame.Save(Path.Combine(dir, $"AVAMarble-{name}.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
    }

    /// <summary>Lets the computer's marble roll until something worth a picture is on screen.</summary>
    private static void RollUntil(GameEngine g, GameCanvas c, Func<GameEngine, bool> done, int maxSeconds = 120)
    {
        for (int t = 0; t < 60 * maxSeconds && !done(g); t++)
        {
            g.Tick(g.Pilot.Decide(g));
            if (t % 3 == 0) c.Advance(1 / 20.0);
        }
        c.Advance(0.016);
    }

    [AvaloniaFact]
    public void The_title_and_the_high_scores()
    {
        var (w, c, g) = Open();
        g.Idle(30);
        c.Advance(0.6);
        Shot(w, c, "title");
        g.HighScores.Insert("RLR", 61250, 6);
        g.HighScores.Insert("AVA", 32000, 4);
        g.Idle((int)(60 * (GameEngine.TitleIdleSeconds + GameEngine.AttractSeconds)) + 10);
        Shot(w, c, "high-scores");
    }

    [AvaloniaFact]
    public void Rolling_the_courses()
    {
        var (w, c, g) = Open(3);
        g.StartGame();
        g.Idle(150);
        c.Advance(0.5);
        Shot(w, c, "course-start");
        RollUntil(g, c, g => g.Phase == Phase.Rolling && g.Player.Y > 15);
        Shot(w, c, "warm-up");
        RollUntil(g, c, g => g.Phase == Phase.Finish && g.PhaseTime > 0.5);
        Shot(w, c, "finish");
        g.StartAt(1);
        g.Go();
        RollUntil(g, c, g => g.Phase == Phase.Rolling && g.Steelies.Any(s => Math.Abs(s.X - g.Player.X) + Math.Abs(s.Y - g.Player.Y) < 2.5));
        Shot(w, c, "steelie");
        g.StartAt(2);
        g.Go();
        RollUntil(g, c, g => g.Phase == Phase.Rolling && g.Player.Y > 17 && g.Player.X > 8);
        Shot(w, c, "acid");
        RollUntil(g, c, g => g.Phase == Phase.Rolling && g.Course.CellAt(g.Player.X, g.Player.Y) is { Kind: CellKind.Wave });
        Shot(w, c, "waves");
        g.StartAt(3);
        g.Go();
        RollUntil(g, c, g => g.Phase == Phase.Rolling && g.Player.Y > 18.5 && g.Player.X > 10);
        Shot(w, c, "sky-walk");
        g.StartAt(4);
        g.Go();
        RollUntil(g, c, g => g.Phase == Phase.Rolling && g.Player.X > 17 && g.Player.Y > 23);
        Shot(w, c, "gulpers");
        g.StartAt(5);
        g.Go();
        RollUntil(g, c, g => g.Phase == Phase.Rolling && g.Player.Y > 30);
        Shot(w, c, "gauntlet");
        // Off the edge, on purpose.
        g.StartAt(3);
        g.Go();
        double top = g.Player.Z;
        for (int i = 0; i < 600 && !(g.Player.Air && g.Player.Z < top - 2.5); i++)
        {
            g.Run(1, new Engine.Controls(1, 0));
            // Draw each frame, as the game does, so the camera stops where the marble left the edge.
            c.InvalidateVisual();
            Pump();
            w.CaptureRenderedFrame();
        }
        Shot(w, c, "fell-off");
    }
}
