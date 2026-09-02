namespace Mahod.Intergreen.Host;

/// <summary>
/// What the engineer must still supply when a project workbook is created from the drawing (David,
/// 2026-08-27, item 4): everything the template needs that is not geometry. The shape is the template's
/// own — one interurban flag and one fast clearing speed per approach (the template derives the slow
/// speed and copies the flag to the turns), a vehicle length per movement, the constants block, a
/// signal-group number per movement and a length per crossing. Nothing here is engineering logic;
/// it is the form that fills the client's sheets.
/// </summary>
public sealed record ApproachInputs(bool Interurban, double FastSpeedKph);

/// <summary>
/// One crossing as the template needs it: its SG letter, its length, and the template slots (1..12) it
/// occupies — decided by where it is in the junction (<see cref="CrossingSlots"/>), never by its letter.
/// A full-width crossing holds both halves of its arm; a crossing nothing crosses holds none.
/// </summary>
public sealed record CrossingInputs(string SignalGroup, double LengthMeters)
{
    public IReadOnlyList<int> Slots { get; init; } = Array.Empty<int>();
}

public sealed record NewProjectInputs(
    IReadOnlyDictionary<string, ApproachInputs> Approaches,          // "N","E","S","W" → inputs
    IReadOnlyDictionary<string, double> VehicleLengthByMovement,     // "N-T" → 12 (default)
    IReadOnlyDictionary<string, string> SignalGroupByMovement,       // "N-T" → "4"
    IReadOnlyDictionary<string, CrossingInputs> Crossings,           // "a" → (SG "a", 12.8 m)
    double PedestrianSpeedMps = NewProjectDefaults.PedestrianSpeedMps,
    double ReactionTimeSec = NewProjectDefaults.ReactionTimeSec,
    double DecelerationMps2 = NewProjectDefaults.DecelerationMps2,
    bool InbarMode = false,
    double InbarDefaultVehicleLengthMeters = NewProjectDefaults.VehicleLengthMeters);

/// <summary>The template's own defaults, read from the blank IG_matrix template (constants block, column I).</summary>
public static class NewProjectDefaults
{
    public const double PedestrianSpeedMps = 1.2;
    public const double ReactionTimeSec = 1.0;
    public const double DecelerationMps2 = 3.5;
    public const double VehicleLengthMeters = 12.0;
    public const double UrbanFastSpeedKph = 50.0;

    /// <summary>The approach letter of a vehicle movement name ("N-T" → "N"); null for crossings and unknowns.</summary>
    public static string? ApproachOf(string movement)
    {
        var dash = movement.IndexOf('-');
        if (dash <= 0) return null;
        var a = movement[..dash].ToUpperInvariant();
        return a is "N" or "E" or "S" or "W" ? a : null;
    }
}
