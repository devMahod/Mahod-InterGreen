# -*- coding: utf-8 -*-
"""Render docs/r14/DAVID_REPORT_r14_HE.md to a Hebrew RTL PDF with the staff-guide stylesheet (Edge headless).

    python docs/r14/render_report.py            -> docs/r14/out/Mahod_Intergreen_r14_דוח_לדייויד.pdf

Minimal Markdown subset used by the report: #/## headings, paragraphs, **bold**, *italic*, `code`,
pipe tables, unordered lists. No third-party modules (python-markdown is not installed here).
"""
import html, re, subprocess, sys, time
from pathlib import Path

HERE = Path(__file__).resolve().parent
SRC = HERE / "DAVID_REPORT_r14_HE.md"
OUT_DIR = HERE / "out"
OUT_HTML = OUT_DIR / "report_r14.html"
OUT_PDF = OUT_DIR / "Mahod_Intergreen_r14_דוח_לדייויד.pdf"
CSS = re.search(r"<style>(.*?)</style>", (HERE.parent / "guides" / "STAFF_GUIDE_HE_r14.html").read_text(encoding="utf-8"), re.S).group(1)


def inline(s):
    s = html.escape(s, quote=False)
    s = re.sub(r"`([^`]+)`", r'<span class="ltr">\1</span>', s)
    s = re.sub(r"\*\*(.+?)\*\*", r"<b>\1</b>", s)
    s = re.sub(r"(?<![\w*])\*(?!\*)(.+?)(?<!\*)\*(?![\w*])", r"<i>\1</i>", s)
    return s


def convert(md):
    out, para, table, lst = [], [], [], []

    def flush_para():
        if para:
            out.append("<p>" + inline(" ".join(para)) + "</p>"); para.clear()

    def flush_table():
        if table:
            rows = [[c.strip() for c in r.strip().strip("|").split("|")] for r in table]
            head, body = rows[0], [r for r in rows[1:] if not all(re.fullmatch(r":?-{2,}:?", c) for c in r)]
            out.append("<table><tr>" + "".join(f"<th>{inline(c)}</th>" for c in head) + "</tr>" +
                       "".join("<tr>" + "".join(f"<td>{inline(c)}</td>" for c in r) + "</tr>" for r in body) + "</table>")
            table.clear()

    def flush_list():
        if lst:
            out.append("<ul>" + "".join(f"<li>{inline(i)}</li>" for i in lst) + "</ul>"); lst.clear()

    for line in md.splitlines():
        if line.startswith("|"):
            flush_para(); flush_list(); table.append(line); continue
        flush_table()
        if line.startswith("- "):
            flush_para(); lst.append(line[2:]); continue
        if not line.strip():
            flush_para(); flush_list(); continue
        m = re.match(r"^(#{1,3})\s+(.*)", line)
        if m:
            flush_para(); flush_list()
            lvl = len(m.group(1))
            text = m.group(2)
            if lvl == 1:
                # "Mahod Intergreen — גרסה r14: ..." -> title + subtitle
                title, _, sub = text.partition(" — ")
                out.append(f"<h1>{inline(title)}</h1><div class='sub'>{inline(sub)}</div>")
            else:
                out.append(f"<h{lvl}>{inline(text)}</h{lvl}>")
            continue
        if lst and line.startswith("  "):
            lst[-1] += " " + line.strip(); continue
        para.append(line.strip())
    flush_para(); flush_table(); flush_list()
    return "\n".join(out)


def main():
    OUT_DIR.mkdir(exist_ok=True)
    body = convert(SRC.read_text(encoding="utf-8"))
    doc = ('<!DOCTYPE html><html lang="he" dir="rtl"><head><meta charset="utf-8">'
           '<title>Mahod Intergreen — r14 — דו"ח לדייויד</title><style>' + CSS +
           ' body { font-size: 10pt; line-height: 1.34; } p { margin: 0 0 1.8mm 0; text-align: right; } h2 { margin-top: 3.5mm; } .foot { margin-top: 3mm; } h2 { page-break-after: avoid; } table { page-break-inside: avoid; }'
           '</style></head><body>' + body +
           '<div class="foot">מהוד הנדסה בע"מ · Mahod Intergreen · Version 0.1.0-r14 · 3.9.2026</div>'
           '</body></html>')
    OUT_HTML.write_text(doc, encoding="utf-8")
    edge = Path(r"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe")
    if not edge.exists():
        edge = Path(r"C:\Program Files\Microsoft\Edge\Application\msedge.exe")
    if OUT_PDF.exists():
        OUT_PDF.unlink()
    subprocess.run([str(edge), "--headless=new", "--disable-gpu", "--no-pdf-header-footer",
                    f"--print-to-pdf={OUT_PDF}", OUT_HTML.as_uri()], capture_output=True, timeout=120)
    for _ in range(20):
        if OUT_PDF.exists() and OUT_PDF.stat().st_size > 0:
            break
        time.sleep(0.5)
    print(OUT_PDF, OUT_PDF.stat().st_size if OUT_PDF.exists() else "MISSING")


if __name__ == "__main__":
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    main()
