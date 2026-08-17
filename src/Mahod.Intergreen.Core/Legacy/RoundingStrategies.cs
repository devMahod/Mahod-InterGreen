namespace Mahod.Intergreen.Core.Legacy;

/// <summary>
/// Final-IG rounding strategy. Two named, selectable strategies exist (Addendum §B.4).
/// The choice is engineering policy — never silently resolved. See ENGINEERING_DECISIONS.md.
/// </summary>
public interface IRoundingStrategy
{
    string Id { get; }

    /// <summary>Rounds a raw intergreen value to whole seconds. Null = UNDEFINED (workbook blank).</summary>
    int? Round(double rawIntergreenSec);
}

/// <summary>
/// David's workbook rule (`mahod-legacy-0.1`):
/// floor when the fraction is below 0.1, otherwise floor + 1.
/// Sub-1-second raw values are UNDEFINED — the workbook yields blank (MOD by zero).
/// </summary>
public sealed class MahodLegacyRounding : IRoundingStrategy
{
    public string Id => "mahod-legacy-0.1";

    public int? Round(double rawIntergreenSec)
    {
        var i = (int)System.Math.Floor(rawIntergreenSec);
        if (i == 0)
            return null; // workbook: MOD(x, 0) → error → blank
        var frac = rawIntergreenSec - i; // rawIntergreenSec is non-negative in every defined case
        return frac < 0.1 ? i : i + 1;
    }
}

/// <summary>
/// §5.7 of the June 2025 guidelines: plain ceiling to whole seconds
/// ("יעוגלו כלפי מעלה לפי שניות שלמות").
/// </summary>
public sealed class GuidelinesCeilingRounding : IRoundingStrategy
{
    /// <summary>
    /// Values within this distance of a whole second round to that second instead of up.
    /// Pedestrian rows compute e.g. 4.8/1.2 = 4.000000000000001 in IEEE doubles; a naive
    /// ceiling would add a full second for 1e-15 of float noise. 1e-9 s is far below any
    /// measurement precision and does not alter any genuine fraction.
    /// Decision recorded in ENGINEERING_DECISIONS.md.
    /// </summary>
    public const double CeilingEpsilon = 1e-9;

    public string Id => "guidelines-ceil";

    public int? Round(double rawIntergreenSec)
        => (int)System.Math.Ceiling(rawIntergreenSec - CeilingEpsilon);
}

/// <summary>
/// Final-IG policy = rounding strategy + optional regulatory minimum.
/// June 2025 §5.6 mandates a 3-second minimum: T = max(3, ceil(...)).
/// Whether the minimum applies to the Legacy pack is an OPEN QUESTION (Addendum §B.5) —
/// the Legacy policy therefore carries no minimum and this must not be changed silently.
/// </summary>
public sealed record FinalIgPolicy(IRoundingStrategy Rounding, int? MinimumSeconds)
{
    public static FinalIgPolicy MahodLegacy { get; } = new(new MahodLegacyRounding(), MinimumSeconds: null);

    public static FinalIgPolicy Guidelines2025 { get; } = new(new GuidelinesCeilingRounding(), MinimumSeconds: 3);

    public int? Resolve(double rawIntergreenSec)
    {
        var rounded = Rounding.Round(rawIntergreenSec);
        if (rounded is null)
            return null;
        return MinimumSeconds is int min && rounded < min ? min : rounded;
    }
}
