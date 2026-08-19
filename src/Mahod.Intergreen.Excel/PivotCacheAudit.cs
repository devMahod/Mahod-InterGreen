using System.Collections.Generic;
using System.Linq;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace Mahod.Intergreen.Excel;

/// <summary>One pivot cache of a workbook, as found in the package (no Excel involved).</summary>
/// <param name="Source">"'Sheet'!A1:C9" for worksheet-sourced caches, else the source type.</param>
/// <param name="WorksheetSourced">True when the cache reads a range of this workbook.</param>
/// <param name="RefreshOnLoad">Excel rebuilds the cache when the workbook opens.</param>
/// <param name="CachedRecords">Number of cached records shipped in xl/pivotCache/pivotCacheRecordsN.xml.</param>
/// <param name="PivotTables">Sheet!Location of every PivotTable rendered from this cache.</param>
public sealed record PivotCacheInfo(
    string Source,
    bool WorksheetSourced,
    bool RefreshOnLoad,
    int CachedRecords,
    IReadOnlyList<string> PivotTables)
{
    /// <summary>A cache that Excel would DISPLAY as shipped: cached records present and no
    /// on-load refresh — after the export changed the underlying sheets, that display is stale
    /// (Lin's r10 finding: "without Refresh everything matches; after Refresh numbers change").</summary>
    public bool IsStaleRisk => WorksheetSourced && CachedRecords > 0 && !RefreshOnLoad;
}

/// <summary>
/// Package-level inspection of pivot caches (r11). Used by the writer's post-conditions, by the
/// regression tests and by diagnostics; it is the "stale pivot cache detection" the export
/// contract relies on.
/// </summary>
public static class PivotCacheAudit
{
    public static IReadOnlyList<PivotCacheInfo> Inspect(string xlsxPath)
    {
        using var doc = SpreadsheetDocument.Open(xlsxPath, false);
        return Inspect(doc);
    }

    public static IReadOnlyList<PivotCacheInfo> Inspect(SpreadsheetDocument doc)
    {
        var wbPart = doc.WorkbookPart!;
        var result = new List<PivotCacheInfo>();
        // cacheId → pivot tables (sheet!location)
        var tablesByCachePart = new Dictionary<PivotTableCacheDefinitionPart, List<string>>();
        foreach (var sheet in wbPart.Workbook.Sheets!.Elements<Sheet>())
        {
            var wsPart = (WorksheetPart)wbPart.GetPartById(sheet.Id!);
            foreach (var pt in wsPart.PivotTableParts)
            {
                var cachePart = pt.PivotTableCacheDefinitionPart;
                if (cachePart is null) continue;
                if (!tablesByCachePart.TryGetValue(cachePart, out var list))
                    tablesByCachePart[cachePart] = list = new List<string>();
                list.Add($"{sheet.Name}!{pt.PivotTableDefinition?.Location?.Reference?.Value}");
            }
        }
        foreach (var cachePart in wbPart.PivotTableCacheDefinitionParts)
        {
            var def = cachePart.PivotCacheDefinition;
            var ws = def?.CacheSource?.WorksheetSource;
            var worksheetSourced = def?.CacheSource?.Type?.Value == SourceValues.Worksheet && ws is not null;
            var source = worksheetSourced
                ? $"'{ws!.Sheet?.Value}'!{ws.Reference?.Value ?? ws.Name?.Value}"
                : (def?.CacheSource?.Type?.Value.ToString() ?? "unknown");
            var records = cachePart.PivotTableCacheRecordsPart?.PivotCacheRecords?.Elements<PivotCacheRecord>().Count() ?? 0;
            tablesByCachePart.TryGetValue(cachePart, out var tables);
            result.Add(new PivotCacheInfo(source, worksheetSourced, def?.RefreshOnLoad?.Value == true, records,
                tables ?? new List<string>()));
        }
        return result;
    }
}
