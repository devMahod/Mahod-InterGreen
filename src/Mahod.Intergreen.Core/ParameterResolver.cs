using System.Collections.Generic;
using Mahod.Intergreen.Contracts;
using Mahod.Intergreen.Rules;

namespace Mahod.Intergreen.Core;

/// <summary>A resolved engineering parameter value together with its source (v3 §19).</summary>
public sealed record ResolvedValue(double Value, string Unit, string Source);

public sealed record ResolvedMovementParameters(
    ResolvedValue FastClearingKph,
    ResolvedValue SlowClearingKph,
    ResolvedValue EnteringKph,
    ResolvedValue VehicleLengthMeters);

/// <summary>
/// Resolves every engineering parameter explicitly from the active rule pack and the
/// project classification. Missing classification blocks with
/// MISSING_ENGINEERING_CLASSIFICATION — it is never guessed (v3 §19).
/// </summary>
public sealed class ParameterResolver
{
    public const string CodeMissingClassification = "IG-CLS-001";
    public const string CodeMissingPostedSpeed = "IG-CLS-002";
    public const string CodeMissingCrossingContext = "IG-CLS-003";

    private readonly RulePack _pack;

    public ParameterResolver(RulePack pack) => _pack = pack;

    public (ResolvedMovementParameters? Parameters, IReadOnlyList<ValidationFinding> Findings)
        ResolveVehicle(Maneuver maneuver, VehicleLengthClass lengthClass, ProjectClassification cls)
    {
        var findings = new List<ValidationFinding>();

        if (cls.RoadType is not RoadEnvironment road)
        {
            findings.Add(new ValidationFinding(CodeMissingClassification, Severity.Error, null,
                "Road environment (urban/interurban) is not classified.",
                RecommendedAction: "Set the project classification before analysis.",
                SourceReference: "Table 5.1 (SR-5.1)"));
            return (null, findings);
        }

        var src = $"RulePack {_pack.Manifest.Id} / Table 5.1 (SR-5.1)";
        var fast = _pack.VehicleSpeedRule(road, maneuver, slow: false).Resolve(cls.PostedSpeedKph);
        var slow = _pack.VehicleSpeedRule(road, maneuver, slow: true).Resolve(cls.PostedSpeedKph);

        if (fast is null || slow is null)
        {
            findings.Add(new ValidationFinding(CodeMissingPostedSpeed, Severity.Error, null,
                $"Speed resolution for {road}/{maneuver} requires the posted speed, which is not set.",
                RecommendedAction: "Enter the posted speed limit in the project classification.",
                SourceReference: "Table 5.1 (SR-5.1)"));
            return (null, findings);
        }

        var length = _pack.VehicleLengthMeters(lengthClass);
        return (new ResolvedMovementParameters(
            new ResolvedValue(fast.Value, "km/h", src),
            new ResolvedValue(slow.Value, "km/h", src),
            new ResolvedValue(fast.Value, "km/h", src + " (SZ = SX per §5.3.1)"),
            new ResolvedValue(length, "m", $"RulePack {_pack.Manifest.Id} / Table 5.5 (SR-5.5)")),
            findings);
    }

    public (ResolvedValue? Speed, IReadOnlyList<ValidationFinding> Findings)
        ResolvePedestrianSpeed(string crossingId, ProjectClassification cls)
    {
        var findings = new List<ValidationFinding>();
        if (!cls.Crossings.TryGetValue(crossingId, out var context))
        {
            findings.Add(new ValidationFinding(CodeMissingCrossingContext, Severity.Error, crossingId,
                $"Crossing '{crossingId}' has no classified context (standard/high-demand/LRT/institutions).",
                RecommendedAction: "Classify the crossing before analysis — the context cannot be detected from geometry.",
                SourceReference: "Table 5.2 (SR-5.2)"));
            return (null, findings);
        }
        var speed = _pack.PedestrianSpeedMps(context);
        return (new ResolvedValue(speed, "m/s", $"RulePack {_pack.Manifest.Id} / Table 5.2 (SR-5.2), context={context}"), findings);
    }
}
