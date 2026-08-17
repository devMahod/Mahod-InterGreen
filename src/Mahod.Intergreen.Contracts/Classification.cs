using System.Collections.Generic;

namespace Mahod.Intergreen.Contracts;

/// <summary>Regulatory road environment (Table 5.1). Determines every speed value.</summary>
public enum RoadEnvironment { Urban, Interurban }

/// <summary>Maneuver kind for speed resolution (Table 5.1 splits through vs turn).</summary>
public enum Maneuver { Through, Turn }

/// <summary>Movement mode. Unknown layers must NEVER silently become Pedestrian (v3 §10).</summary>
public enum MovementMode { Vehicle, Bus, Sherut, Lrt, Bicycle, Pedestrian }

/// <summary>Crossing context (Table 5.2): standard 1.2 m/s, the rest 1.0 m/s.</summary>
public enum CrossingContext { Standard, HighDemand, CrossesLrt, NearInstitutions }

/// <summary>Vehicle length class (Table 5.5).</summary>
public enum VehicleLengthClass { Standard12, ArticulatedHeavy19, Bicycle2, Lrt15 }

/// <summary>
/// Project-level engineering classification. Analysis is BLOCKED while required
/// fields are missing (MISSING_ENGINEERING_CLASSIFICATION) — never guessed (v3 §19).
/// </summary>
public sealed record ProjectClassification
{
    public RoadEnvironment? RoadType { get; init; }
    public double? PostedSpeedKph { get; init; }
    public bool InbarMode { get; init; }
    public IReadOnlyDictionary<string, CrossingContext> Crossings { get; init; }
        = new Dictionary<string, CrossingContext>();
}
