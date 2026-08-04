using MDEdit.Editing;

namespace MDEdit.Tests;

public class ZoomLevelsTests
{
    // ── In: next multiple of 10% above ────────────────────────────────────
    [Theory]
    [InlineData(1.00, 1.10)]
    [InlineData(0.10, 0.20)]
    [InlineData(1.40, 1.50)]
    [InlineData(4.90, 5.00)]
    public void In_MovesUpOneStep(double from, double expected)
        => Assert.Equal(expected, ZoomLevels.In(from), precision: 4);

    // The floating-point trap this class exists to avoid: 0.7 / 0.1 is 6.999999999999999, so a
    // Math.Floor-based "next multiple" hands back 0.7 again and zooming in silently stops working.
    [Theory]
    [InlineData(0.30)]
    [InlineData(0.70)]
    [InlineData(0.80)]
    [InlineData(2.90)]
    public void In_AlwaysAdvances(double from)
        => Assert.True(ZoomLevels.In(from) > from, $"In({from}) did not advance");

    [Theory]
    [InlineData(1.00, 0.90)]
    [InlineData(0.20, 0.10)]
    [InlineData(5.00, 4.90)]
    public void Out_MovesDownOneStep(double from, double expected)
        => Assert.Equal(expected, ZoomLevels.Out(from), precision: 4);

    [Theory]
    [InlineData(0.30)]
    [InlineData(0.70)]
    [InlineData(0.80)]
    [InlineData(3.00)]
    public void Out_AlwaysDescends(double from)
        => Assert.True(ZoomLevels.Out(from) < from, $"Out({from}) did not descend");

    // ── Off-grid presets re-align rather than adding 10 points ────────────
    // 75% and 125% are reachable from the status-bar drop-down but are not on the 10% grid, so
    // stepping from one has to land on a grid value: 75 -> 80, not 85.
    [Theory]
    [InlineData(0.75, 0.80, 0.70)]
    [InlineData(1.25, 1.30, 1.20)]
    public void OffGridLevels_ReAlignToTheGrid(double from, double expectedIn, double expectedOut)
    {
        Assert.Equal(expectedIn, ZoomLevels.In(from), precision: 4);
        Assert.Equal(expectedOut, ZoomLevels.Out(from), precision: 4);
    }

    // ── Clamping at both ends ─────────────────────────────────────────────
    [Fact]
    public void In_ClampsAtMax() => Assert.Equal(ZoomLevels.Max, ZoomLevels.In(ZoomLevels.Max), precision: 4);

    [Fact]
    public void Out_ClampsAtMin() => Assert.Equal(ZoomLevels.Min, ZoomLevels.Out(ZoomLevels.Min), precision: 4);

    [Fact]
    public void In_And_Out_AreInverseOnGridValues()
    {
        for (int percent = 20; percent <= 490; percent += 10)
        {
            double level = percent / 100.0;
            Assert.Equal(level, ZoomLevels.Out(ZoomLevels.In(level)), precision: 4);
            Assert.Equal(level, ZoomLevels.In(ZoomLevels.Out(level)), precision: 4);
        }
    }

    // ── Sanitize: settings.json is hand-editable ──────────────────────────
    [Theory]
    [InlineData(0.05, 0.10)]   // below the floor
    [InlineData(0.00, 0.10)]
    [InlineData(-3.0, 0.10)]   // negative
    [InlineData(12.0, 5.00)]   // above the ceiling
    public void Sanitize_ClampsOutOfRange(double stored, double expected)
        => Assert.Equal(expected, ZoomLevels.Sanitize(stored), precision: 4);

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void Sanitize_FallsBackToDefaultForNonFinite(double stored)
        => Assert.Equal(ZoomLevels.Default, ZoomLevels.Sanitize(stored), precision: 4);

    // Grid-snapping here would quietly turn a 75% preset into 80% on the next launch, so Sanitize
    // rounds to a whole percent and stops there.
    [Theory]
    [InlineData(0.75)]
    [InlineData(1.25)]
    public void Sanitize_PreservesOffGridPresets(double stored)
        => Assert.Equal(stored, ZoomLevels.Sanitize(stored), precision: 4);

    [Fact]
    public void Sanitize_RoundsToWholePercent()
        => Assert.Equal(1.23, ZoomLevels.Sanitize(1.2345), precision: 4);

    [Fact]
    public void Sanitize_LeavesValidLevelsAlone()
    {
        foreach (double preset in ZoomLevels.Presets)
            Assert.Equal(preset, ZoomLevels.Sanitize(preset), precision: 4);
    }

    // ── The presets the status bar offers ─────────────────────────────────
    [Fact]
    public void Presets_AreInRangeAndAscending()
    {
        Assert.NotEmpty(ZoomLevels.Presets);
        for (int i = 0; i < ZoomLevels.Presets.Count; i++)
        {
            Assert.InRange(ZoomLevels.Presets[i], ZoomLevels.Min, ZoomLevels.Max);
            if (i > 0) Assert.True(ZoomLevels.Presets[i] > ZoomLevels.Presets[i - 1]);
        }
    }

    [Fact]
    public void Presets_IncludeDefault() => Assert.Contains(ZoomLevels.Default, ZoomLevels.Presets);

    [Fact]
    public void Default_IsInRange() => Assert.InRange(ZoomLevels.Default, ZoomLevels.Min, ZoomLevels.Max);

    // Walking the whole ladder must terminate at Max, not stall part-way — the guard against the
    // floating-point failure above reappearing at some level the InlineData above doesn't name.
    [Fact]
    public void In_ReachesMaxFromMinInFiniteSteps()
    {
        double level = ZoomLevels.Min;
        int steps = 0;
        while (level < ZoomLevels.Max && steps < 1000)
        {
            level = ZoomLevels.In(level);
            steps++;
        }

        Assert.Equal(ZoomLevels.Max, level, precision: 4);
        Assert.Equal(49, steps); // 10% -> 500% in 10-point steps
    }
}
