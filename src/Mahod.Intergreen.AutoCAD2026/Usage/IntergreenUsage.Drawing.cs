using Autodesk.AutoCAD.ApplicationServices;

namespace Mahod.Intergreen.AutoCAD2026;

/// <summary>The one usage call that needs a drawing (see IntergreenUsage.cs).</summary>
internal static partial class IntergreenUsage
{
    /// <summary>A junction's intergreen matrix was exported from <paramref name="doc"/>: one priced unit.</summary>
    public static void Exported(Document doc, string workbookPath)
    {
        string drawing;
        try
        {
            drawing = doc.Database.FingerprintGuid;
        }
        catch (System.Exception)
        {
            return;
        }
        Junction(drawing, workbookPath);
    }
}
