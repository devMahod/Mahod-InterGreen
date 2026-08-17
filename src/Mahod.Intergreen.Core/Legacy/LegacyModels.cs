namespace Mahod.Intergreen.Core.Legacy;

/// <summary>
/// Constants block of David's workbook. Units are explicit in the names.
/// Defaults per both golden workbooks: ped 1.2 m/s, reaction 1.0 s, decel 3.5 m/s².
/// </summary>
public sealed record LegacyConstants(
    double PedestrianSpeedMps = 1.2,
    double ReactionTimeSec = 1.0,
    double DecelerationMps2 = 3.5,
    double? GlobalVehicleLengthMeters = null,
    bool InbarMode = false,
    double InbarDefaultVehicleLengthMeters = 12.0);

/// <summary>Per-movement row of the Parameters sheet. Null = cell empty / non-numeric.</summary>
public sealed record LegacyMovementParameters(
    double? FastClearingKph,
    double? SlowClearingKph,
    double? VehicleLengthMeters,
    double? AdditionToInbarMeters);

/// <summary>
/// Template variant of the workbook wiring (Addendum §C.2).
/// V1 (Example 1): per-movement vehicle length via VLOOKUP column E; INBAR flag present.
/// V2 (Example 2): one global vehicle length in the constants block.
/// </summary>
public enum LegacyTemplateVariant { V1PerMovementVehicleLength, V2GlobalVehicleLength }

/// <summary>One measured conflict point. Null = blank cell (NOT zero — see Addendum §C.3).</summary>
public readonly record struct MeasuredPoint(double? ClearingDistanceMeters, double? EnteringDistanceMeters)
{
    public bool IsPopulated => ClearingDistanceMeters is not null || EnteringDistanceMeters is not null;
}

/// <summary>Input for one workbook conflict row.</summary>
public sealed record ConflictRowInput(
    int ConflictNo,
    string ClearingMovement,
    string EnteringMovement,
    MeasuredPoint Point1,
    MeasuredPoint Point2,
    MeasuredPoint Point3,
    MeasuredPoint Point4)
{
    public MeasuredPoint[] Points => new[] { Point1, Point2, Point3, Point4 };
}

/// <summary>Per-point computed times. Null when the workbook leaves the point blank.</summary>
public sealed record PointTimes(double ClearFastSec, double ClearSlowSec, double EnterSec, double IntergreenSec);

/// <summary>Result of the workbook-compatibility calculation for one row.</summary>
public sealed record LegacyRowResult(
    double ClearingSpeedFastMps,
    double ClearingSpeedSlowMps,
    double EnteringSpeedMps,
    double? VehicleLengthMeters,
    PointTimes?[] PointResults,
    double? RawMaxIntergreenSec,
    int? DefiningPointIndex,
    int? FinalIg,
    bool ManualRoundingCandidate);
