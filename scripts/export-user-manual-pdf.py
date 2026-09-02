#!/usr/bin/env python3
"""Convert docs/使用说明书.md to PDF using Edge headless."""

from __future__ import annotations

import html
import re
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
DOCS = ROOT / "docs"
MD_FILE = DOCS / "使用说明书.md"
HTML_FILE = DOCS / "使用说明书.html"
PDF_FILE = DOCS / "使用说明书.pdf"
CSPROJ = ROOT / "src" / "HuaGuang.Monitor" / "HuaGuang.Monitor.csproj"

EDGE_CANDIDATES = [
    Path(r"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe"),
    Path(r"C:\Program Files\Microsoft\Edge\Application\msedge.exe"),
]


def find_edge() -> Path:
    for path in EDGE_CANDIDATES:
        if path.exists():
            return path
    raise FileNotFoundError("Microsoft Edge not found.")


def inline_md(text: str) -> str:
    text = html.escape(text)
    text = re.sub(r"\*\*(.+?)\*\*", r"<strong>\1</strong>", text)
    text = re.sub(r"`([^`]+)`", r"<code>\1</code>", text)
    return text


def convert_markdown(md: str) -> str:
    lines = md.splitlines()
    out: list[str] = []
    i = 0

    def flush_paragraph(buffer: list[str]) -> None:
        if not buffer:
            return
        text = " ".join(part.strip() for part in buffer if part.strip())
        if text:
            out.append(f"<p>{inline_md(text)}</p>")
        buffer.clear()

    para: list[str] = []

    while i < len(lines):
        line = lines[i]
        stripped = line.strip()

        if not stripped:
            flush_paragraph(para)
            i += 1
            continue

        if stripped == "---":
            flush_paragraph(para)
            out.append("<hr/>")
            i += 1
            continue

        if stripped.startswith("#"):
            flush_paragraph(para)
            level = len(stripped) - len(stripped.lstrip("#"))
            title = stripped[level:].strip()
            out.append(f"<h{min(level, 4)}>{inline_md(title)}</h{min(level, 4)}>")
            i += 1
            continue

        if stripped.startswith(">"):
            flush_paragraph(para)
            out.append(f"<blockquote>{inline_md(stripped[1:].strip())}</blockquote>")
            i += 1
            continue

        if stripped.startswith("!["):
            flush_paragraph(para)
            match = re.match(r"!\[(.*?)\]\((.*?)\)", stripped)
            if match:
                alt, src = match.groups()
                out.append(
                    f'<figure><img src="{html.escape(src)}" alt="{html.escape(alt)}"/>'
                    f'<figcaption>{html.escape(alt)}</figcaption></figure>'
                )
            i += 1
            continue

        if stripped.startswith("|"):
            flush_paragraph(para)
            table_lines: list[str] = []
            while i < len(lines) and lines[i].strip().startswith("|"):
                table_lines.append(lines[i].strip())
                i += 1
            if len(table_lines) >= 2:
                headers = [c.strip() for c in table_lines[0].strip("|").split("|")]
                rows = []
                for row_line in table_lines[2:]:
                    rows.append([c.strip() for c in row_line.strip("|").split("|")])
                out.append("<table><thead><tr>")
                for h in headers:
                    out.append(f"<th>{inline_md(h)}</th>")
                out.append("</tr></thead><tbody>")
                for row in rows:
                    out.append("<tr>")
                    for cell in row:
                        out.append(f"<td>{inline_md(cell)}</td>")
                    out.append("</tr>")
                out.append("</tbody></table>")
            continue

        if stripped.startswith("```"):
            flush_paragraph(para)
            lang = stripped[3:].strip()
            i += 1
            code_lines: list[str] = []
            while i < len(lines) and not lines[i].strip().startswith("```"):
                code_lines.append(lines[i])
                i += 1
            i += 1
            code = html.escape("\n".join(code_lines))
            cls = f' class="language-{html.escape(lang)}"' if lang else ""
            out.append(f"<pre><code{cls}>{code}</code></pre>")
            continue

        if stripped.startswith("- "):
            flush_paragraph(para)
            out.append("<ul>")
            while i < len(lines) and lines[i].strip().startswith("- "):
                item = lines[i].strip()[2:].strip()
                out.append(f"<li>{inline_md(item)}</li>")
                i += 1
            out.append("</ul>")
            continue

        if re.match(r"^\d+\.\s", stripped):
            flush_paragraph(para)
            out.append("<ol>")
            while i < len(lines) and re.match(r"^\d+\.\s", lines[i].strip()):
                item = re.sub(r"^\d+\.\s*", "", lines[i].strip())
                out.append(f"<li>{inline_md(item)}</li>")
                i += 1
            out.append("</ol>")
            continue

        para.append(line)
        i += 1

    flush_paragraph(para)
    return "\n".join(out)


def read_app_version() -> tuple[str, str]:
    import json

    try:
        result = subprocess.run(
            [
                "dotnet",
                "msbuild",
                str(CSPROJ),
                "-getProperty:ApplicationDisplayVersion",
                "-getProperty:ApplicationVersion",
                "-nologo",
            ],
            capture_output=True,
            text=True,
            check=True,
            timeout=60,
        )
        payload = json.loads(result.stdout)
        version = payload["Properties"]["ApplicationDisplayVersion"].strip()
        revision = payload["Properties"]["ApplicationVersion"].strip()
        if version and revision:
            return version, revision
    except Exception:
        pass

    text = CSPROJ.read_text(encoding="utf-8")
    display = re.search(r"<ApplicationDisplayVersion>([^<]+)</ApplicationDisplayVersion>", text)
    build = re.search(r"<ApplicationVersion>([^<]+)</ApplicationVersion>", text)
    version = display.group(1).strip() if display else "1.0.0"
    revision = build.group(1).strip() if build else "1"
    return version, revision


def build_html(body: str, version: str, revision: str) -> str:
    version_label = f"{version}（修订 {revision}）"
    return f"""<!DOCTYPE html>
<html lang="zh-CN">
<head>
<meta charset="utf-8"/>
<title>工业监控 — 使用说明书 {version_label}</title>
<style>
@page {{ margin: 18mm 16mm; }}
body {{
  font-family: "Segoe UI", "Microsoft YaHei", sans-serif;
  color: #1a1a1a;
  line-height: 1.6;
  font-size: 11pt;
  max-width: 920px;
  margin: 0 auto;
  padding: 24px;
}}
h1 {{ color: #0B1522; border-bottom: 3px solid #2EC4B6; padding-bottom: 8px; font-size: 24pt; }}
h2 {{ color: #0F2740; margin-top: 28px; font-size: 16pt; page-break-after: avoid; }}
h3 {{ color: #1F3A52; margin-top: 20px; font-size: 13pt; page-break-after: avoid; }}
h4 {{ color: #1F3A52; margin-top: 16px; font-size: 12pt; }}
p, li {{ margin: 6px 0; }}
blockquote {{
  border-left: 4px solid #2EC4B6;
  margin: 12px 0;
  padding: 8px 14px;
  background: #f4f8fb;
  color: #334;
}}
code {{
  background: #eef2f6;
  padding: 1px 5px;
  border-radius: 4px;
  font-family: Consolas, monospace;
  font-size: 10pt;
}}
pre {{
  background: #0B1522;
  color: #C9D6E2;
  padding: 14px;
  border-radius: 8px;
  overflow-x: auto;
  font-size: 9pt;
  page-break-inside: avoid;
}}
pre code {{ background: transparent; color: inherit; padding: 0; }}
table {{
  width: 100%;
  border-collapse: collapse;
  margin: 12px 0 18px;
  font-size: 10pt;
  page-break-inside: avoid;
}}
th, td {{
  border: 1px solid #cfd8e3;
  padding: 8px 10px;
  text-align: left;
  vertical-align: top;
}}
th {{ background: #101C28; color: #fff; }}
tr:nth-child(even) td {{ background: #f7fafc; }}
figure {{
  margin: 16px 0 24px;
  page-break-inside: avoid;
  text-align: center;
}}
figure img {{
  max-width: 100%;
  border: 1px solid #d0d8e0;
  border-radius: 8px;
  box-shadow: 0 4px 16px rgba(0,0,0,.08);
}}
figcaption {{
  margin-top: 8px;
  color: #667;
  font-size: 10pt;
}}
hr {{ border: none; border-top: 1px solid #dde4ea; margin: 24px 0; }}
ul, ol {{ padding-left: 22px; }}
</style>
</head>
<body>
<p><strong>版本 {version_label}</strong></p>
{body}
</body>
</html>
"""


def export_pdf() -> None:
    if not MD_FILE.exists():
        raise FileNotFoundError(f"Markdown not found: {MD_FILE}")

    version, revision = read_app_version()
    md = MD_FILE.read_text(encoding="utf-8")
    body = convert_markdown(md)
    HTML_FILE.write_text(build_html(body, version, revision), encoding="utf-8")

    edge = find_edge()
    html_uri = HTML_FILE.resolve().as_uri()
    pdf_path = str(PDF_FILE.resolve())
    output_pdf = PDF_FILE

    if PDF_FILE.exists():
        try:
            PDF_FILE.unlink()
        except OSError:
            output_pdf = DOCS / "使用说明书-更新.pdf"
            pdf_path = str(output_pdf.resolve())
            if output_pdf.exists():
                output_pdf.unlink()

    cmd = [
        str(edge),
        "--headless=new",
        "--disable-gpu",
        "--no-pdf-header-footer",
        f"--print-to-pdf={pdf_path}",
        html_uri,
    ]
    result = subprocess.run(cmd, capture_output=True, text=True, timeout=120)
    if result.returncode != 0 or not output_pdf.exists():
        raise RuntimeError(
            f"PDF export failed (code {result.returncode}).\n"
            f"stdout: {result.stdout}\nstderr: {result.stderr}"
        )

    size_kb = output_pdf.stat().st_size // 1024
    print(f"Generated: {output_pdf} ({size_kb} KB) · {version}（修订 {revision}）")


if __name__ == "__main__":
    try:
        export_pdf()
    except Exception as ex:
        print(f"Error: {ex}", file=sys.stderr)
        sys.exit(1)
