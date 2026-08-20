using System.Text.Json;
using Mahod.Intergreen.Host;

namespace Mahod.Intergreen.Host.Tests;

/// <summary>
/// r12: the endpoint-reference confirmation the engine has always honoured but the palette never
/// exposed. Covers the ordering/wording the engineer reads and the sidecar round-trip the engine
/// reads back. The geometric detection itself lives in the AutoCAD layer and is proven headlessly
/// by IG_SMOKE_REFERENCE_REVIEW against a real drawing.
/// </summary>
public class ReferenceReviewTests
{
    private static ReferenceIssue Issue(string mv, string curve, string handle, double gapM)
        => new(mv, curve, handle, gapM);

    [Fact]
    public void Widest_gap_first_so_the_likeliest_drawing_defect_leads()
    {
        var sorted = ReferenceReview.Sorted(new[]
        {
            Issue("E-T", "E-T.b1", "4EBA", 0.0474),
            Issue("N-R", "N-R.b2", "4F27", 0.0625),
            Issue("S-L", "S-L.b1", "5357", 0.0054),
        });
        Assert.Equal(new[] { "N-R.b2", "E-T.b1", "S-L.b1" }, sorted.Select(i => i.CurveId));
    }

    [Fact]
    public void Equal_gaps_break_ties_by_curve_id_so_the_list_is_stable()
    {
        var sorted = ReferenceReview.Sorted(new[]
        {
            Issue("W-T", "W-T.b2", "57B4", 0.02),
            Issue("S-R", "S-R.b1", "5065", 0.02),
        });
        Assert.Equal(new[] { "S-R.b1", "W-T.b2" }, sorted.Select(i => i.CurveId));
    }

    [Fact]
    public void Line_states_the_gap_in_centimetres_and_the_dwg_handle()
    {
        var line = ReferenceReview.Line(Issue("E-T", "E-T.b1", "4EBA", 0.0474));
        Assert.Contains("E-T.b1", line);
        Assert.Contains("4EBA", line);
        Assert.Contains("4.7", line);
    }

    [Fact]
    public void Summary_names_the_movements_because_a_count_alone_is_not_actionable()
    {
        var summary = ReferenceReview.HebrewSummary(new[]
        {
            Issue("E-T", "E-T.b1", "4EBA", 0.0474),
            Issue("N-R", "N-R.b2", "4F27", 0.0625),
        });
        Assert.Contains("E-T", summary);
        Assert.Contains("N-R", summary);
        Assert.Contains("2", summary);
    }

    [Fact]
    public void Summary_is_empty_when_nothing_is_pending()
        => Assert.Equal("", ReferenceReview.HebrewSummary(Array.Empty<ReferenceIssue>()));

    [Fact]
    public void Two_movements_reported_once_each_even_with_two_boundaries_each()
    {
        var summary = ReferenceReview.HebrewSummary(new[]
        {
            Issue("E-T", "E-T.b1", "1", 0.04),
            Issue("E-T", "E-T.b2", "2", 0.03),
        });
        Assert.Equal(1, summary.Split("E-T").Length - 1);
    }
}

public class SidecarStringArrayTests : IDisposable
{
    private readonly string _dir = TestPaths.NewTempDir("refsidecar");
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    [Fact]
    public void Confirmations_round_trip_through_the_sidecar()
    {
        string path = Path.Combine(_dir, "p.intergreen-project.json");
        var data = new Dictionary<string, JsonElement>();
        SidecarStore.SetStrings(data, ReferenceReview.SidecarKey, new[] { "E-T.b1", "N-R.b2" });
        SidecarStore.Commit(path, data);

        var reloaded = SidecarStore.Load(path);
        Assert.Equal(new[] { "E-T.b1", "N-R.b2" },
            SidecarStore.GetStrings(reloaded.Data, ReferenceReview.SidecarKey));
    }

    [Fact]
    public void Re_confirming_the_same_boundary_does_not_duplicate_it()
    {
        var data = new Dictionary<string, JsonElement>();
        SidecarStore.SetStrings(data, ReferenceReview.SidecarKey, new[] { "E-T.b1", "E-T.b1", "N-R.b2" });
        Assert.Equal(new[] { "E-T.b1", "N-R.b2" },
            SidecarStore.GetStrings(data, ReferenceReview.SidecarKey));
    }

    [Fact]
    public void Order_is_canonical_so_an_unchanged_set_rewrites_an_identical_file()
    {
        string a = Path.Combine(_dir, "a.json"), b = Path.Combine(_dir, "b.json");
        var d1 = new Dictionary<string, JsonElement>();
        var d2 = new Dictionary<string, JsonElement>();
        SidecarStore.SetStrings(d1, ReferenceReview.SidecarKey, new[] { "N-R.b2", "E-T.b1" });
        SidecarStore.SetStrings(d2, ReferenceReview.SidecarKey, new[] { "E-T.b1", "N-R.b2" });
        SidecarStore.Commit(a, d1);
        SidecarStore.Commit(b, d2);
        Assert.Equal(File.ReadAllText(a), File.ReadAllText(b));
    }

    [Fact]
    public void A_sidecar_without_the_key_yields_no_confirmations_rather_than_throwing()
        => Assert.Empty(SidecarStore.GetStrings(new Dictionary<string, JsonElement>(), ReferenceReview.SidecarKey));

    [Fact]
    public void A_malformed_value_is_ignored_and_never_silently_confirms_anything()
    {
        var data = new Dictionary<string, JsonElement>();
        SidecarStore.Set(data, ReferenceReview.SidecarKey, "E-T.b1");   // string, not an array
        Assert.Empty(SidecarStore.GetStrings(data, ReferenceReview.SidecarKey));
    }

    [Fact]
    public void Confirmations_survive_alongside_the_workbook_reference()
    {
        string path = Path.Combine(_dir, "both.json");
        var data = new Dictionary<string, JsonElement>();
        SidecarStore.Set(data, "workbook", @"C:\p\IG_matrix.xlsx");
        SidecarStore.SetStrings(data, ReferenceReview.SidecarKey, new[] { "E-T.b1" });
        SidecarStore.Commit(path, data);

        var reloaded = SidecarStore.Load(path);
        Assert.Equal(new[] { "E-T.b1" }, SidecarStore.GetStrings(reloaded.Data, ReferenceReview.SidecarKey));
        Assert.Equal(@"C:\p\IG_matrix.xlsx", reloaded.Data["workbook"].GetString());
    }
}
