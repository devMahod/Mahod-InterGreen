using System.Drawing;

namespace MahodIntergreenSetup;

/// <summary>Single-window Hebrew (RTL) install wizard: Welcome → progress → Success.</summary>
internal sealed class InstallerForm : Form
{
    internal static int ExitCode = 2; // cancelled unless install completes

    private readonly Label _title = new();
    private readonly Label _subtitle = new();
    private readonly Label _body = new();
    private readonly Label _detect = new();
    private readonly Label _status = new();
    private readonly Button _install = new();
    private readonly Button _close = new();

    public InstallerForm()
    {
        Text = Program.ProductName + " — התקנה";
        RightToLeft = RightToLeft.Yes;
        RightToLeftLayout = true;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(560, 400);
        BackColor = Color.White;
        Font = new Font("Segoe UI", 10f);

        _title.Text = "Mahod Intergreen";
        _title.Font = new Font("Segoe UI Semibold", 20f);
        _title.ForeColor = Color.FromArgb(20, 60, 110);
        _title.SetBounds(20, 24, 520, 44);
        _title.TextAlign = ContentAlignment.MiddleRight;

        _subtitle.Text = "חישוב זמנים בין-ירוקים · מהוד הנדסה · גרסת מנוע " + Program.EngineVersion;
        _subtitle.Font = new Font("Segoe UI", 10.5f);
        _subtitle.ForeColor = Color.FromArgb(90, 90, 90);
        _subtitle.SetBounds(20, 70, 520, 26);
        _subtitle.TextAlign = ContentAlignment.MiddleRight;

        _body.Text =
            "האשף יתקין את תוסף Mahod Intergreen עבור AutoCAD 2026.\n\n" +
            "• התקנה ברמת המשתמש בלבד — ללא הרשאות מנהל.\n" +
            "• מותקן לתיקיית התוספים הרשמית של Autodesk ‏(ApplicationPlugins).\n" +
            "• לא נוגע בשרטוטים, בפרויקטים או בתוספים אחרים.\n" +
            "• הסרה: הגדרות Windows ← אפליקציות ← Mahod Intergreen ← הסרה.\n\n" +
            "לאחר ההתקנה: פתחו את AutoCAD 2026 והקלידו INTERGREEN.";
        _body.SetBounds(20, 108, 520, 170);
        _body.TextAlign = ContentAlignment.TopRight;

        bool acad = Program.AutoCad2026Detected();
        _detect.Text = acad
            ? "✓ AutoCAD 2026 זוהה במחשב זה."
            : "‼ AutoCAD 2026 לא זוהה במחשב זה. אפשר להתקין בכל זאת — התוסף ייטען כשיותקן AutoCAD 2026.";
        _detect.ForeColor = acad ? Color.FromArgb(0, 130, 60) : Color.FromArgb(190, 120, 0);
        _detect.Font = new Font("Segoe UI Semibold", 10f);
        _detect.SetBounds(20, 282, 520, 40);
        _detect.TextAlign = ContentAlignment.TopRight;

        _status.Text = "";
        _status.ForeColor = Color.FromArgb(20, 60, 110);
        _status.SetBounds(20, 322, 380, 26);
        _status.TextAlign = ContentAlignment.MiddleRight;

        _install.Text = "התקנה";
        _install.Font = new Font("Segoe UI Semibold", 10.5f);
        _install.SetBounds(456, 352, 84, 34);
        _install.Click += OnInstall;

        _close.Text = "ביטול";
        _close.SetBounds(360, 352, 84, 34);
        _close.Click += (_, _) => Close();

        Controls.AddRange([_title, _subtitle, _body, _detect, _status, _install, _close]);
        AcceptButton = _install;
    }

    private void OnInstall(object? sender, EventArgs e)
    {
        _install.Enabled = false;
        _close.Enabled = false;
        try
        {
            Program.Install(msg =>
            {
                _status.Text = msg;
                _status.Refresh();
            });
            ExitCode = 0;
            _body.Text =
                "ההתקנה הושלמה בהצלחה!\n\n" +
                "השלבים הבאים:\n" +
                "1. פתחו את AutoCAD 2026 (אם היה פתוח — סגרו ופתחו מחדש).\n" +
                "2. פתחו את שרטוט הבין-ירוקים.\n" +
                "3. בשורת הפקודה הקלידו: INTERGREEN\n\n" +
                "להסרה: הגדרות Windows ← אפליקציות ← Mahod Intergreen.";
            _detect.Text = "✓ Mahod Intergreen מותקן.";
            _detect.ForeColor = Color.FromArgb(0, 130, 60);
            _status.Text = "";
            _close.Text = "סיום";
            _close.Enabled = true;
            _install.Visible = false;
            AcceptButton = _close;
        }
        catch (Exception ex)
        {
            ExitCode = 1;
            _status.Text = "";
            MessageBox.Show("ההתקנה נכשלה:\n" + ex.Message, Program.ProductName,
                MessageBoxButtons.OK, MessageBoxIcon.Error,
                MessageBoxDefaultButton.Button1, MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign);
            _install.Enabled = true;
            _close.Enabled = true;
        }
    }
}
