using System.Text.Json;
using Mahod.Intergreen.Host;

namespace Mahod.Intergreen.Host.Tests;

/// <summary>
/// ED-016 (David, 2026-08-27, item 6b): a near-miss stop-line reference under a project-configured
/// tolerance is confirmed automatically — visibly, logged, and kept apart from the engineer's own
/// confirmations. These cover the partition rule and the sidecar plumbing; the pipeline hook is
/// proven headlessly on the real drawings by IG_SMOKE_REFERENCE_REVIEW.
/// </summary>
public class AutoConfirmToleranceTests
{
    private static ReferenceIssue Issue(string mv, string curve, double gapM) => new(mv, curve, "H", gapM);

    [Fact]
    public void Default_tolerance_is_davids_ten_centimetres()
        => Assert.Equal(0.10, ReferenceReview.DefaultAutoConfirmToleranceMeters, 9);

    [Fact]
    public void Lins_two_cases_fall_under_the_default_and_a_sixteen_cm_gap_does_not()
    {
        var (auto, pending) = ReferenceReview.Partition(new[]
        {
            Issue("E-T", "E-T.b1", 0.0474),
            Issue("N-R", "N-R.b2", 0.0625),
            Issue("E-L", "E-L.b1", 0.1645),
        }, ReferenceReview.DefaultAutoConfirmToleranceMeters);
        Assert.Equal(new[] { "N-R.b2", "E-T.b1" }, auto.Select(i => i.CurveId));
        Assert.Equal(new[] { "E-L.b1" }, pending.Select(i => i.CurveId));
    }

    [Fact]
    public void A_gap_exactly_on_the_tolerance_is_confirmed()
    {
        var (auto, pending) = ReferenceReview.Partition(new[] { Issue("A", "A.b1", 0.10) }, 0.10);
        Assert.Single(auto);
        Assert.Empty(pending);
    }

    [Fact]
    public void Tolerance_zero_confirms_nothing()
    {
        var (auto, pending) = ReferenceReview.Partition(new[] { Issue("A", "A.b1", 0.001) }, 0.0);
        Assert.Empty(auto);
        Assert.Single(pending);
    }

    [Fact]
    public void A_typo_of_five_metres_is_clamped_to_the_half_metre_ceiling()
    {
        var (auto, pending) = ReferenceReview.Partition(new[]
        {
            Issue("A", "A.b1", 0.45),
            Issue("B", "B.b1", 0.60),
        }, 5.0);
        Assert.Equal(new[] { "A.b1" }, auto.Select(i => i.CurveId));
        Assert.Equal(new[] { "B.b1" }, pending.Select(i => i.CurveId));
    }

    [Fact]
    public void Summary_names_the_movements_and_the_threshold()
    {
        var text = ReferenceReview.AutoConfirmedSummary(new[]
        {
            Issue("E-T", "E-T.b1", 0.0474), Issue("N-R", "N-R.b2", 0.0625),
        }, 0.10);
        Assert.Contains("E-T", text);
        Assert.Contains("N-R", text);
        Assert.Contains("10", text);
        Assert.Equal("", ReferenceReview.AutoConfirmedSummary(Array.Empty<ReferenceIssue>(), 0.10));
    }
}

public class SidecarNumericSettingTests : IDisposable
{
    private readonly string _dir = TestPaths.NewTempDir("tolsidecar");
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    [Fact]
    public void Tolerance_round_trips_through_the_sidecar()
    {
        string path = Path.Combine(_dir, "p.intergreen-project.json");
        var data = new Dictionary<string, JsonElement>();
        SidecarStore.SetDouble(data, ReferenceReview.ToleranceSidecarKey, 0.10);
        SidecarStore.Commit(path, data);
        Assert.Equal(0.10, SidecarStore.GetDouble(SidecarStore.Load(path).Data, ReferenceReview.ToleranceSidecarKey));
    }

    [Fact]
    public void Missing_tolerance_reads_as_null_so_the_caller_applies_the_default_explicitly()
        => Assert.Null(SidecarStore.GetDouble(new Dictionary<string, JsonElement>(), ReferenceReview.ToleranceSidecarKey));

    [Fact]
    public void A_hand_edited_string_value_is_still_read()
    {
        var data = new Dictionary<string, JsonElement>();
        SidecarStore.Set(data, ReferenceReview.ToleranceSidecarKey, "0.05");
        Assert.Equal(0.05, SidecarStore.GetDouble(data, ReferenceReview.ToleranceSidecarKey));
    }

    [Fact]
    public void Garbage_reads_as_null_rather_than_zero()
    {
        var data = new Dictionary<string, JsonElement>();
        SidecarStore.Set(data, ReferenceReview.ToleranceSidecarKey, "ten");
        Assert.Null(SidecarStore.GetDouble(data, ReferenceReview.ToleranceSidecarKey));
    }

    [Fact]
    public void Auto_confirmations_are_stored_apart_from_the_engineers_own()
    {
        string path = Path.Combine(_dir, "prov.intergreen-project.json");
        var data = new Dictionary<string, JsonElement>();
        SidecarStore.SetStrings(data, ReferenceReview.SidecarKey, new[] { "E-T.b1", "N-R.b2", "Q.b1" });
        SidecarStore.SetStrings(data, ReferenceReview.AutoConfirmedSidecarKey, new[] { "E-T.b1", "N-R.b2" });
        SidecarStore.Commit(path, data);
        var back = SidecarStore.Load(path).Data;
        Assert.Equal(3, SidecarStore.GetStrings(back, ReferenceReview.SidecarKey).Count);
        Assert.Equal(new[] { "E-T.b1", "N-R.b2" }, SidecarStore.GetStrings(back, ReferenceReview.AutoConfirmedSidecarKey));
    }
}
