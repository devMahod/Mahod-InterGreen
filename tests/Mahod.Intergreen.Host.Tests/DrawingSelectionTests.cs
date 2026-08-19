using Mahod.Intergreen.Host;

namespace Mahod.Intergreen.Host.Tests;

/// <summary>r10 (Lin's feedback): the project DWG is chosen through a normal file picker.
/// Decision logic, initial directory, cancel/invalid/valid/already-open flows and state
/// preservation are pinned here without any Autodesk host.</summary>
public class DrawingSelectionTests
{
    private static readonly string Active = @"C:\Projects\Pines\PINNES.dwg";
    private static readonly string Other = @"C:\Projects\Pines\OTHER.dwg";
    private static readonly string Elsewhere = @"D:\Archive\old.dwg";
    private static bool Exists(string p) => p.Equals(Active, StringComparison.OrdinalIgnoreCase)
        || p.Equals(Other, StringComparison.OrdinalIgnoreCase)
        || p.Equals(Elsewhere, StringComparison.OrdinalIgnoreCase)
        || p.EndsWith(@"\notes.txt", StringComparison.OrdinalIgnoreCase);

    // ---------------- Evaluate (pure decision) ----------------

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Cancel_or_empty_pick_changes_nothing(string? picked)
    {
        var r = DrawingSelection.Evaluate(picked, Active, new[] { Active }, Exists);
        Assert.Equal(DrawingSelectionStatus.Cancelled, r.Status);
        Assert.Null(r.NormalizedPath);
        Assert.False(r.ChangedActiveDrawing);
        Assert.False(r.IsError);
        Assert.Contains("בוטלה", r.UserMessageHe);
        Assert.Contains("לא השתנו", r.UserMessageHe);
    }

    [Fact]
    public void Non_dwg_file_is_rejected_in_hebrew_and_changes_nothing()
    {
        var r = DrawingSelection.Evaluate(@"C:\Projects\Pines\notes.txt", Active, new[] { Active }, Exists);
        Assert.Equal(DrawingSelectionStatus.NotADrawing, r.Status);
        Assert.True(r.IsError);
        Assert.False(r.ChangedActiveDrawing);
        Assert.Contains(".dwg", r.UserMessageHe);
        Assert.Contains("אינו שרטוט DWG", r.UserMessageHe);
        Assert.Contains("notes.txt", r.UserMessageHe);
    }

    [Fact]
    public void Missing_dwg_is_rejected_in_hebrew_with_the_path_named()
    {
        var r = DrawingSelection.Evaluate(@"C:\Projects\Pines\GONE.dwg", Active, new[] { Active }, Exists);
        Assert.Equal(DrawingSelectionStatus.NotFound, r.Status);
        Assert.True(r.IsError);
        Assert.Contains("לא נמצא", r.UserMessageHe);
        Assert.Contains(@"C:\Projects\Pines\GONE.dwg", r.UserMessageHe);
    }

    [Fact]
    public void Picking_the_active_drawing_again_is_a_no_op_not_an_error()
    {
        // different casing + quotes, as a pasted path may arrive
        var r = DrawingSelection.Evaluate("\"c:\\projects\\pines\\pinnes.DWG\"", Active, new[] { Active }, Exists);
        Assert.Equal(DrawingSelectionStatus.AlreadyActive, r.Status);
        Assert.False(r.IsError);
        Assert.False(r.ChangedActiveDrawing);
        Assert.Contains("כבר פתוח ופעיל", r.UserMessageHe);
    }

    [Fact]
    public void Drawing_already_open_elsewhere_is_activated_not_reopened()
    {
        var r = DrawingSelection.Evaluate(Other, Active, new[] { Active, Other }, Exists);
        Assert.Equal(DrawingSelectionStatus.ActivatedOpen, r.Status);
        Assert.True(r.ChangedActiveDrawing);
        Assert.Equal(Other, r.NormalizedPath);
        Assert.Contains("Setup", r.UserMessageHe);
    }

    [Fact]
    public void Valid_new_drawing_is_opened()
    {
        var r = DrawingSelection.Evaluate(Elsewhere, Active, new[] { Active }, Exists);
        Assert.Equal(DrawingSelectionStatus.Opened, r.Status);
        Assert.True(r.ChangedActiveDrawing);
        Assert.Equal(Elsewhere, r.NormalizedPath);
        Assert.Contains("נפתח", r.UserMessageHe);
        Assert.Contains("Setup", r.UserMessageHe);
    }

    [Fact]
    public void Valid_drawing_with_no_active_document_is_opened()
    {
        var r = DrawingSelection.Evaluate(Elsewhere, null, Array.Empty<string>(), Exists);
        Assert.Equal(DrawingSelectionStatus.Opened, r.Status);
    }

    // ---------------- initial directory ----------------

    [Fact]
    public void Initial_directory_prefers_recent_folder_then_active_drawing_then_documents()
    {
        bool DirExists(string d) => d is @"C:\Recent" or @"C:\Projects\Pines";
        Assert.Equal(@"C:\Recent", DrawingSelection.InitialDirectory(@"C:\Recent", Active, @"C:\Docs", DirExists));
        Assert.Equal(@"C:\Projects\Pines", DrawingSelection.InitialDirectory(@"C:\Gone", Active, @"C:\Docs", DirExists));
        Assert.Equal(@"C:\Projects\Pines", DrawingSelection.InitialDirectory(null, Active, @"C:\Docs", DirExists));
        Assert.Equal(@"C:\Docs", DrawingSelection.InitialDirectory(null, null, @"C:\Docs", DirExists));
        Assert.Equal(@"C:\Docs", DrawingSelection.InitialDirectory(@"C:\Gone", @"E:\nowhere\x.dwg", @"C:\Docs", DirExists));
    }

    // ---------------- bound-drawing label ----------------

    [Fact]
    public void Bound_drawing_label_shows_the_file_name_or_an_actionable_hint()
    {
        Assert.Equal("שרטוט: PINNES.dwg", DrawingSelection.BoundDrawingLabelHe(Active));
        Assert.Contains("אין שרטוט פתוח", DrawingSelection.BoundDrawingLabelHe(null));
        Assert.Contains("שרטוט DWG", DrawingSelection.BoundDrawingLabelHe(""));
    }

    // ---------------- chooser (view-model) with fakes ----------------

    private sealed class FakePicker : IDrawingPicker
    {
        public string? Result;
        public string? LastInitialDirectory;
        public int Calls;
        public string? PickDrawing(string? initialDirectory) { Calls++; LastInitialDirectory = initialDirectory; return Result; }
    }

    private sealed class FakeHost : IDrawingHost
    {
        public string? ActiveDrawingPath { get; set; }
        public List<string> Open_ { get; } = new();
        public IReadOnlyList<string> OpenDrawingPaths => Open_;
        public List<string> Activated { get; } = new();
        public List<string> Opened { get; } = new();
        public Exception? ThrowOnOpen;
        public void Activate(string p) { Activated.Add(p); ActiveDrawingPath = p; }
        public void Open(string p)
        {
            if (ThrowOnOpen is not null) throw ThrowOnOpen;
            Opened.Add(p); Open_.Add(p); ActiveDrawingPath = p;
        }
    }

    private sealed class FakeStore : IRecentFolderStore
    {
        public string? Value;
        public int Saves;
        public string? Load() => Value;
        public void Save(string folder) { Saves++; Value = folder; }
    }

    private static DrawingChooser Chooser(FakePicker picker, FakeHost host, FakeStore store)
        => new(picker, host, store, Exists, d => d is @"C:\Recent" or @"C:\Projects\Pines" or @"D:\Archive", @"C:\Docs");

    [Fact]
    public void Chooser_cancel_makes_no_host_call_and_no_store_write()
    {
        var picker = new FakePicker { Result = null };
        var host = new FakeHost { ActiveDrawingPath = Active, Open_ = { Active } };
        var store = new FakeStore { Value = @"C:\Recent" };
        var r = Chooser(picker, host, store).Choose();

        Assert.Equal(DrawingSelectionStatus.Cancelled, r.Status);
        Assert.Equal(1, picker.Calls);
        Assert.Equal(@"C:\Recent", picker.LastInitialDirectory); // sensible initial directory
        Assert.Empty(host.Opened);
        Assert.Empty(host.Activated);
        Assert.Equal(Active, host.ActiveDrawingPath);           // bound drawing preserved
        Assert.Equal(0, store.Saves);
        Assert.Equal(@"C:\Recent", store.Value);
    }

    [Fact]
    public void Chooser_invalid_file_reports_hebrew_error_and_preserves_state()
    {
        var picker = new FakePicker { Result = @"C:\Projects\Pines\notes.txt" };
        var host = new FakeHost { ActiveDrawingPath = Active, Open_ = { Active } };
        var store = new FakeStore();
        var r = Chooser(picker, host, store).Choose();

        Assert.Equal(DrawingSelectionStatus.NotADrawing, r.Status);
        Assert.True(r.IsError);
        Assert.Empty(host.Opened);
        Assert.Empty(host.Activated);
        Assert.Equal(Active, host.ActiveDrawingPath);
        Assert.Equal(0, store.Saves);
    }

    [Fact]
    public void Chooser_valid_selection_opens_the_drawing_and_remembers_its_folder()
    {
        var picker = new FakePicker { Result = Elsewhere };
        var host = new FakeHost { ActiveDrawingPath = Active, Open_ = { Active } };
        var store = new FakeStore();
        var r = Chooser(picker, host, store).Choose();

        Assert.Equal(DrawingSelectionStatus.Opened, r.Status);
        Assert.Equal(new[] { Elsewhere }, host.Opened);
        Assert.Empty(host.Activated);
        Assert.Equal(Elsewhere, host.ActiveDrawingPath);
        Assert.Equal(1, store.Saves);
        Assert.Equal(@"D:\Archive", store.Value);
        // the initial directory fell back to the active drawing's folder (no recent folder yet)
        Assert.Equal(@"C:\Projects\Pines", picker.LastInitialDirectory);
    }

    [Fact]
    public void Chooser_already_open_drawing_is_activated_without_reopening()
    {
        var picker = new FakePicker { Result = Other };
        var host = new FakeHost { ActiveDrawingPath = Active, Open_ = { Active, Other } };
        var store = new FakeStore();
        var r = Chooser(picker, host, store).Choose();

        Assert.Equal(DrawingSelectionStatus.ActivatedOpen, r.Status);
        Assert.Equal(new[] { Other }, host.Activated);
        Assert.Empty(host.Opened);
        Assert.Equal(Other, host.ActiveDrawingPath);
    }

    [Fact]
    public void Chooser_reselecting_the_active_drawing_is_a_no_op()
    {
        var picker = new FakePicker { Result = Active.ToLowerInvariant() };
        var host = new FakeHost { ActiveDrawingPath = Active, Open_ = { Active } };
        var store = new FakeStore();
        var r = Chooser(picker, host, store).Choose();

        Assert.Equal(DrawingSelectionStatus.AlreadyActive, r.Status);
        Assert.Empty(host.Opened);
        Assert.Empty(host.Activated);
        Assert.Equal(Active, host.ActiveDrawingPath);
    }

    [Fact]
    public void Chooser_host_open_failure_becomes_a_hebrew_error_not_a_crash()
    {
        var picker = new FakePicker { Result = Elsewhere };
        var host = new FakeHost { ActiveDrawingPath = Active, Open_ = { Active }, ThrowOnOpen = new IOException("locked") };
        var store = new FakeStore();
        var r = Chooser(picker, host, store).Choose();

        Assert.Equal(DrawingSelectionStatus.OpenFailed, r.Status);
        Assert.True(r.IsError);
        Assert.Contains("לא ניתן לפתוח", r.UserMessageHe);
        Assert.Contains("old.dwg", r.UserMessageHe);
        Assert.Contains("locked", r.LogDetail);
        Assert.Equal(Active, host.ActiveDrawingPath); // still bound to the previous drawing
        Assert.Equal(0, store.Saves);
    }

    // ---------------- recent folder store ----------------

    [Fact]
    public void Recent_folder_store_round_trips_and_tolerates_missing_file()
    {
        string dir = TestPaths.NewTempDir("ig-recent");
        try
        {
            var store = new RecentDrawingFolderStore(Path.Combine(dir, "sub", "recent-dwg-folder.txt"));
            Assert.Null(store.Load());
            store.Save(@"C:\Projects\Pines");
            Assert.Equal(@"C:\Projects\Pines", store.Load());
            store.Save(@"D:\Archive");
            Assert.Equal(@"D:\Archive", new RecentDrawingFolderStore(store.FilePath).Load());
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Recent_folder_store_never_throws_on_unwritable_location()
    {
        var store = new RecentDrawingFolderStore(@"Z:\definitely\not\here\<>|\recent.txt");
        store.Save(@"C:\x"); // must not throw
        Assert.Null(store.Load());
    }
}
