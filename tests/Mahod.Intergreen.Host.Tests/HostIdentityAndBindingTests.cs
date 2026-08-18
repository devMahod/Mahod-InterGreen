using Mahod.Intergreen.Host;

namespace Mahod.Intergreen.Host.Tests;

/// <summary>r9 regressions: real host-product identification and palette-to-drawing
/// binding (stale cross-document results).</summary>
public class HostProductIdentityTests
{
    [Fact]
    public void Civil_is_detected_from_loaded_AECC_modules_not_from_branding()
    {
        // the real Civil 3D 2027 case: PRODUCT reports "AutoCAD" but AECC modules loaded
        var loaded = new[] { "acdbmgd", "accoremgd", "AeccDbMgd", "Mahod.Intergreen.Host" };
        Assert.Equal("Civil 3D", HostProductIdentity.Detect(loaded, "AutoCAD"));
    }

    [Fact]
    public void Plain_AutoCAD_stays_AutoCAD()
    {
        var loaded = new[] { "acdbmgd", "accoremgd", "acmgd", "Mahod.Intergreen.Host" };
        Assert.Equal("AutoCAD", HostProductIdentity.Detect(loaded, "AutoCAD"));
    }

    [Fact]
    public void Missing_reported_product_defaults_to_AutoCAD_and_nulls_are_tolerated()
    {
        var loaded = new string?[] { null, "acdbmgd" };
        Assert.Equal("AutoCAD", HostProductIdentity.Detect(loaded, null));
        Assert.Equal("AutoCAD", HostProductIdentity.Detect(loaded, "  "));
    }

    [Fact]
    public void Aecc_prefix_match_is_case_insensitive()
    {
        Assert.Equal("Civil 3D", HostProductIdentity.Detect(new[] { "aeccpressurepipesmgd" }, "AutoCAD"));
    }
}

public class DrawingBindingTests
{
    [Fact]
    public void Switching_to_a_different_drawing_requires_reset()
    {
        Assert.True(DrawingBinding.RequiresReset(@"C:\work\ex1.dwg", @"C:\work\ex2.dwg"));
    }

    [Fact]
    public void Same_drawing_does_not_reset_even_with_case_or_path_form_differences()
    {
        Assert.False(DrawingBinding.RequiresReset(@"C:\Work\EX1.DWG", @"C:\work\ex1.dwg"));
        Assert.False(DrawingBinding.RequiresReset(@"C:\work\sub\..\ex1.dwg", @"C:\work\ex1.dwg"));
    }

    [Fact]
    public void First_activation_and_no_active_drawing_both_reset()
    {
        Assert.True(DrawingBinding.RequiresReset(null, @"C:\work\ex1.dwg"));
        Assert.True(DrawingBinding.RequiresReset(@"C:\work\ex1.dwg", null));
        Assert.True(DrawingBinding.RequiresReset(null, null));
    }

    [Fact]
    public void Document_switch_invalidates_all_workflow_state_so_stale_results_cannot_be_acted_on()
    {
        // the exact r8 field note being closed: analyzed state from drawing A must be
        // fully gated after switching to drawing B until B is set up / re-analyzed.
        var sm = new WorkflowStateMachine();
        sm.OnSetupCommitted();
        sm.OnAnalyzeSucceeded();
        sm.HasSelection = true;
        sm.OnProjectInvalidated(); // what BindToActiveDrawing calls on switch
        Assert.False(sm.ProjectConfigured);
        Assert.False(sm.Analyzed);
        Assert.NotNull(sm.Gate(WorkflowAction.Show));
        Assert.NotNull(sm.Gate(WorkflowAction.Export));
        Assert.NotNull(sm.Gate(WorkflowAction.Validate));
    }
}
