using AVAMarble.Engine;
using Engine = AVAMarble.Engine;

namespace AVAMarble.Tests;

public class GameplayTests
{
    private static string TempFolder() => Path.Combine(Path.GetTempPath(), "avamarble-tests-" + Guid.NewGuid().ToString("N"));

    /// <summary>A game with the count down skipped and no steelies or gulpers unless a test adds them.</summary>
    private static GameEngine Game(int course = 0)
    {
        var g = new GameEngine(7, HighScoreTable.Load(TempFolder())) { Silent = true, Calm = true };
        g.StartGame();
        g.StartAt(course);
        g.Go();
        return g;
    }

    /// <summary>A game on a little test course of the test's own.</summary>
    private static GameEngine On(Course c)
    {
        var g = Game();
        c.Time = 60;
        g.UseCourse(c);
        return g;
    }

    private static Engine.Controls Stick(int dx = 0, int dy = 0, bool brake = false) => new(dx, dy, brake);

    // ───────────────────────────── rolling ─────────────────────────────

    [Fact]
    public void On_the_flat_a_marble_stays_put_until_pushed_and_then_slows()
    {
        var g = On(new Course(10, 10) { StartX = 5, StartY = 5 }.Flat(0, 0, 10, 10, 5));
        g.Idle(60);
        Assert.Equal(5, g.Player.X, 6);
        Assert.Equal(5, g.Player.Y, 6);
        // Right on the screen is +X and -Y on the course.
        g.Run(20, Stick(dx: 1));
        Assert.True(g.Player.Vx > 0.5 && g.Player.Vy < -0.5);
        double speed = g.Player.Speed;
        g.Idle(60);
        Assert.True(g.Player.Speed < speed);
        g.Run(30, Stick(brake: true));
        Assert.True(g.Player.Speed < 0.3);
    }

    [Fact]
    public void A_marble_rolls_down_a_ramp_on_its_own()
    {
        var g = On(new Course(6, 20) { StartX = 3, StartY = 1.5 }.Flat(0, 0, 6, 1, 10).Ramp(0, 1, 6, 15, 'y', 10, 4).Flat(0, 16, 6, 4, 4));
        g.Idle(90);
        Assert.True(g.Player.Y > 5);
        Assert.True(g.Player.Z < 9);
        Assert.Equal(g.Course.Floor(g.Player.X, g.Player.Y)!.Value, g.Player.Z, 3);
    }

    [Fact]
    public void A_wall_bounces_the_marble_back()
    {
        var g = On(new Course(10, 6) { StartX = 2, StartY = 3 }.Flat(0, 0, 10, 6, 5).Flat(6, 0, 4, 6, 7));
        g.Run(60, Stick(dx: 1, dy: 1));
        Assert.True(g.Player.X < 6);
        Assert.Equal(5, g.Player.Z, 3);
    }

    [Fact]
    public void A_small_drop_is_a_hop_and_a_big_one_breaks_the_marble()
    {
        var small = On(new Course(10, 6) { StartX = 2, StartY = 3 }.Flat(0, 0, 5, 6, 6).Flat(5, 0, 5, 6, 4.5));
        small.Run(50, Stick(dx: 1, dy: 1));
        small.Idle(30);
        Assert.Equal(Phase.Rolling, small.Phase);
        Assert.True(small.Player.X > 5);
        var big = On(new Course(10, 6) { StartX = 2, StartY = 3 }.Flat(0, 0, 5, 6, 9).Flat(5, 0, 5, 6, 4));
        big.Run(50, Stick(dx: 1, dy: 1));
        big.Idle(40);
        Assert.Equal(Phase.Mishap, big.Phase);
        Assert.Equal("BROKEN!", big.Mishap);
    }

    [Fact]
    public void Off_the_edge_the_marble_falls_and_a_new_one_comes_back_on_safe_ground()
    {
        var g = On(new Course(10, 10) { StartX = 5, StartY = 5 }.Flat(2, 2, 6, 6, 5));
        g.Idle(30);
        g.Run(120, Stick(dx: 1));
        for (int i = 0; i < 120 && g.Phase == Phase.Rolling; i++) g.Run(1, Stick(dx: 1));
        Assert.Equal(Phase.Mishap, g.Phase);
        Assert.Equal("FELL OFF!", g.Mishap);
        double time = g.TimeLeft;
        g.Idle((int)(60 * GameEngine.MishapSeconds) + 2);
        Assert.Equal(Phase.Rolling, g.Phase);
        Assert.NotNull(g.Course.Floor(g.Player.X, g.Player.Y));
        Assert.True(g.TimeLeft < time - GameEngine.MishapSeconds + 0.1, "the clock runs while a new marble comes");
    }

    [Fact]
    public void Acid_dissolves_the_marble()
    {
        var g = On(new Course(10, 10) { StartX = 2, StartY = 5 }.Flat(0, 0, 10, 10, 5).Mark(5, 0, 2, 10, CellKind.Acid));
        g.Run(90, Stick(dx: 1, dy: 1));
        Assert.Equal("DISSOLVED!", g.Mishap);
    }

    [Fact]
    public void Waves_rise_and_fall_with_time()
    {
        var c = new Course(6, 6) { StartX = 1, StartY = 1 }.Flat(0, 0, 6, 6, 5, CellKind.Wave);
        var heights = Enumerable.Range(0, 20).Select(i => c.Floor(3, 3, i * 0.1)!.Value).ToList();
        Assert.True(heights.Max() - heights.Min() > 0.5);
    }

    // ───────────────────────────── the opposition ─────────────────────────────

    [Fact]
    public void A_steelie_hunts_the_marble_and_knocks_it_about()
    {
        var g = On(new Course(14, 14) { StartX = 7, StartY = 7 }.Flat(0, 0, 14, 14, 5));
        var s = g.AddSteelie(10, 10);
        double d0 = Math.Abs(s.X - 7) + Math.Abs(s.Y - 7);
        g.Idle(40);
        Assert.True(Math.Abs(s.X - g.Player.X) + Math.Abs(s.Y - g.Player.Y) < d0);
        for (int i = 0; i < 240 && g.Player.Speed < 0.5; i++) g.Idle(1);
        Assert.True(g.Player.Speed >= 0.5, "the steelie should have hit the marble");
    }

    [Fact]
    public void A_gulper_swallows_the_marble()
    {
        var g = On(new Course(10, 10) { StartX = 2, StartY = 5 }.Flat(0, 0, 10, 10, 5));
        // It hops along towards a marble sitting still.
        g.AddGulper(7, 5, 2, 5);
        for (int i = 0; i < 600 && g.Phase == Phase.Rolling; i++) g.Idle(1);
        Assert.Equal("GULPED!", g.Mishap);
    }

    // ───────────────────────────── the clock and the courses ─────────────────────────────

    [Fact]
    public void Reaching_the_goal_scores_and_the_spare_time_carries_over()
    {
        var g = On(new Course(10, 6) { StartX = 2, StartY = 3 }.Flat(0, 0, 7, 6, 5).Flat(7, 0, 3, 6, 5, CellKind.Goal));
        g.TimeLeft = 30;
        g.Run(80, Stick(dx: 1, dy: 1));
        Assert.Equal(Phase.Finish, g.Phase);
        Assert.InRange(g.Carried, 28, 30);
        Assert.Equal(1000 + (int)g.Carried * 100, g.Score);
        g.Idle(60 * 5);
        Assert.Equal(1, g.CourseIndex);
        Assert.Equal(Phase.CourseStart, g.Phase);
        Assert.Equal(g.Course.Time + g.Carried, g.TimeLeft, 3);
    }

    [Fact]
    public void Out_of_time_the_game_is_over()
    {
        var g = Game();
        g.TimeLeft = 1;
        g.Idle(70);
        Assert.Equal(Phase.TimeUp, g.Phase);
        g.Idle(60 * 4);
        Assert.Equal(GameState.GameOver, g.State);
    }

    [Fact]
    public void The_count_down_holds_the_clock_and_the_marble()
    {
        var g = new GameEngine(3, HighScoreTable.Load(TempFolder())) { Silent = true };
        g.Tick(default, start: true);
        Assert.Equal(Phase.CourseStart, g.Phase);
        double t = g.TimeLeft;
        g.Run(120, Stick(dx: 1));
        Assert.Equal(t, g.TimeLeft);
        Assert.Equal(0, g.Player.Speed);
        g.Idle(120);
        Assert.Equal(Phase.Rolling, g.Phase);
    }

    [Fact]
    public void Every_course_starts_on_solid_ground_and_ends_in_a_goal()
    {
        for (int i = 0; i < Courses.Count; i++)
        {
            var c = Courses.Make(i);
            Assert.NotNull(c.Floor(c.StartX, c.StartY));
            var end = c.Path[^1];
            Assert.Equal(CellKind.Goal, c.CellAt(end.X, end.Y)?.Kind);
            foreach (var w in c.Path) Assert.True(c.Floor(w.X, w.Y) is not null, $"{c.Name}: waypoint {w} is over nothing");
        }
    }

    [Fact]
    public void The_computer_marble_finishes_every_course_in_time()
    {
        for (int i = 0; i < Courses.Count; i++)
        {
            var g = new GameEngine(5, HighScoreTable.Load(TempFolder())) { Silent = true };
            g.StartGame();
            g.StartAt(i);
            g.Go();
            for (int t = 0; t < 60 * 120 && g.Phase is Phase.Rolling or Phase.Mishap; t++) g.Tick(g.Pilot.Decide(g));
            Assert.True(g.Phase == Phase.Finish, $"{g.Course.Name}: {g.Phase} {g.Mishap}");
        }
    }

    [Fact]
    public void All_six_courses_win_the_game()
    {
        var g = Game(Courses.Count - 1);
        var goal = g.Course.Path[^1];
        g.Place(goal.X, goal.Y);
        g.Idle(2);
        Assert.Equal(Phase.Finish, g.Phase);
        g.Idle(60 * 5);
        Assert.Equal(Phase.Victory, g.Phase);
        g.Idle(60 * 6);
        Assert.Equal(GameState.GameOver, g.State);
    }

    // ───────────────────────────── the machine ─────────────────────────────

    [Fact]
    public void The_attract_mode_plays_itself_then_shows_the_high_scores()
    {
        var g = new GameEngine(5, HighScoreTable.Load(TempFolder())) { Silent = true };
        g.Idle((int)(60 * GameEngine.TitleIdleSeconds) + 2);
        Assert.Equal(GameState.Attract, g.State);
        var start = (g.Player.X, g.Player.Y);
        g.Idle(60 * 8);
        Assert.NotEqual(start, (g.Player.X, g.Player.Y));
        g.Idle((int)(60 * GameEngine.AttractSeconds));
        Assert.Equal(GameState.HighScores, g.State);
    }

    [Fact]
    public void A_good_score_asks_for_initials()
    {
        var g = Game();
        g.Score = 99999;
        g.TimeLeft = 0.5;
        g.Idle(60 * 8);
        Assert.Equal(GameState.EnterInitials, g.State);
        g.Initials!.Type('R');
        g.Initials.Type('O');
        g.Initials.Type('L');
        g.Initials.Finish();
        g.Idle(1);
        Assert.Equal(GameState.HighScores, g.State);
        Assert.Equal(new HighScoreEntry("ROL", 99999, 1), g.HighScores.Entries[0]);
    }

    [Fact]
    public void The_same_seed_plays_the_same_game()
    {
        string Play()
        {
            var g = new GameEngine(9, HighScoreTable.Load(TempFolder())) { Silent = true };
            g.StartGame();
            for (int t = 0; t < 60 * 40; t++) g.Tick(g.Pilot.Decide(g));
            return $"{g.Score}:{g.CourseIndex}:{g.Player.X:0.000}:{g.Player.Y:0.000}";
        }
        Assert.Equal(Play(), Play());
    }

    [Fact]
    public void High_scores_persist_in_their_folder()
    {
        string folder = TempFolder();
        var t = HighScoreTable.Load(folder);
        Assert.Equal(HighScoreTable.Size, t.Entries.Count);
        t.Insert("BIG", 123456, 6);
        t.Save();
        Assert.Equal(new HighScoreEntry("BIG", 123456, 6), HighScoreTable.Load(folder).Entries[0]);
    }
}
