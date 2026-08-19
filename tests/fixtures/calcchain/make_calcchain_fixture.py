# -*- coding: utf-8 -*-
"""Builds tests/fixtures/calcchain/calcchain-regression.xlsx — a minimal, hand-written OOXML
workbook that CONTAINS an xl/calcChain.xml (like David's real IG_matrix workbooks saved by
Excel). 'Input Distances'!D3 is a formula cell that the exporter replaces with an engine value;
F3 and 'Other'!A1 are formulas that must survive untouched. The chain lists all three.
Deterministic: fixed timestamps, stored (uncompressed) entries."""
import zipfile
from pathlib import Path

OUT = Path(__file__).with_name("calcchain-regression.xlsx")

CT = """<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/><Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/><Override PartName="/xl/worksheets/sheet2.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/><Override PartName="/xl/calcChain.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.calcChain+xml"/><Override PartName="/xl/sharedStrings.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sharedStrings+xml"/><Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/></Types>"""

RELS = """<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/></Relationships>"""

WB = """<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><sheets><sheet name="Input Distances" sheetId="1" r:id="rId1"/><sheet name="Other" sheetId="2" r:id="rId2"/></sheets><calcPr calcId="191029"/></workbook>"""

WB_RELS = """<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/><Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet2.xml"/><Relationship Id="rId3" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/><Relationship Id="rId4" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/sharedStrings" Target="sharedStrings.xml"/><Relationship Id="rId5" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/calcChain" Target="calcChain.xml"/></Relationships>"""

# Row 2 = header, row 3 = one conflict row X→Y: D3 formula (replaced by the export),
# E3 constant, F3 formula (must be preserved), G3 constant.
SHEET1 = """<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><sheetData><row r="2"><c r="B2" t="s"><v>2</v></c><c r="C2" t="s"><v>3</v></c><c r="D2" t="s"><v>4</v></c><c r="E2" t="s"><v>5</v></c></row><row r="3"><c r="B3" t="s"><v>0</v></c><c r="C3" t="s"><v>1</v></c><c r="D3"><f>10+2</f><v>12</v></c><c r="E3"><v>3</v></c><c r="F3"><f>D3*2</f><v>24</v></c><c r="G3"><v>4</v></c></row></sheetData></worksheet>"""

SHEET2 = """<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><sheetData><row r="1"><c r="A1"><f>1+2</f><v>3</v></c><c r="B1"><v>7</v></c></row></sheetData></worksheet>"""

# Excel's own cached chain: every formula cell, i = sheetId.
CALC = """<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<calcChain xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><c r="D3" i="1"/><c r="F3" i="1"/><c r="A1" i="2"/></calcChain>"""

SST = """<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<sst xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" count="6" uniqueCount="6"><si><t>X</t></si><si><t>Y</t></si><si><t>Clearing</t></si><si><t>Entering</t></si><si><t>CD1</t></si><si><t>ED1</t></si></sst>"""

STYLES = """<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><fonts count="1"><font><sz val="11"/><name val="Calibri"/></font></fonts><fills count="2"><fill><patternFill patternType="none"/></fill><fill><patternFill patternType="gray125"/></fill></fills><borders count="1"><border><left/><right/><top/><bottom/><diagonal/></border></borders><cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs><cellXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/></cellXfs></styleSheet>"""

parts = [
    ("[Content_Types].xml", CT),
    ("_rels/.rels", RELS),
    ("xl/workbook.xml", WB),
    ("xl/_rels/workbook.xml.rels", WB_RELS),
    ("xl/worksheets/sheet1.xml", SHEET1),
    ("xl/worksheets/sheet2.xml", SHEET2),
    ("xl/calcChain.xml", CALC),
    ("xl/sharedStrings.xml", SST),
    ("xl/styles.xml", STYLES),
]
OUT.parent.mkdir(parents=True, exist_ok=True)
with zipfile.ZipFile(OUT, "w", zipfile.ZIP_DEFLATED) as z:
    for name, body in parts:
        zi = zipfile.ZipInfo(name, date_time=(2026, 1, 1, 0, 0, 0))
        zi.compress_type = zipfile.ZIP_DEFLATED
        zi.external_attr = 0o644 << 16
        z.writestr(zi, body.encode("utf-8"))
print("wrote", OUT, OUT.stat().st_size, "bytes")

import openpyxl, warnings; warnings.simplefilter("ignore")
wb = openpyxl.load_workbook(OUT)
print([ws.title for ws in wb.worksheets], wb["Input Distances"]["D3"].value, wb["Input Distances"]["F3"].value, wb["Other"]["A1"].value)
with zipfile.ZipFile(OUT) as z:
    print("calcChain present:", "xl/calcChain.xml" in z.namelist())
