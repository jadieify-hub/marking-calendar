"""Collect supported public ЧЗ product lists, independently of calendar dates."""
import argparse
from datetime import datetime, timezone
import hashlib
from html.parser import HTMLParser
import json
import os
from pathlib import Path
import re
import sys
from urllib.parse import urlsplit, urlunsplit
from urllib.request import Request, urlopen

ROOT = Path(__file__).resolve().parents[1]
PILOT_IDS = ("homeware", "cosmetics", "grocery", "children")
CHZ_HOST = "xn--80ajghhoc2aj1c8b.xn--p1ai"
HEADINGS = {"homeware": "товаров для дома", "cosmetics": "Виды косметики", "grocery": "Виды бакалейной", "children": "виды товаров для детей"}
VOID_TAGS = {"area", "base", "br", "col", "embed", "hr", "img", "input", "link", "meta", "param", "source", "track", "wbr"}


class Node:
    def __init__(self, tag, attrs=()):
        self.tag, self.attrs, self.children = tag, dict(attrs), []

    def has_class(self, value):
        return value in self.attrs.get("class", "").split()

    def nodes(self):
        yield self
        for child in self.children:
            if isinstance(child, Node):
                yield from child.nodes()

    def text(self):
        if self.tag in ("script", "style"):
            return ""
        parts = []
        for child in self.children:
            parts.append(child.text() if isinstance(child, Node) else re.sub(r"\s+", " ", child))
        result = "".join(parts)
        if self.tag in ("br", "p", "li", "div", "tr"):
            result = "\n" + result + "\n"
        return result


class Page(HTMLParser):
    def __init__(self, html):
        super().__init__(convert_charrefs=True)
        self.root = Node("root")
        self.stack = [self.root]
        self.feed(html)

    def handle_starttag(self, tag, attrs):
        node = Node(tag, attrs)
        self.stack[-1].children.append(node)
        if tag not in VOID_TAGS:
            self.stack.append(node)

    def handle_startendtag(self, tag, attrs):
        self.handle_starttag(tag, attrs)
        if tag not in VOID_TAGS:
            self.handle_endtag(tag)

    def handle_endtag(self, tag):
        for i in range(len(self.stack) - 1, 0, -1):
            if self.stack[i].tag == tag:
                del self.stack[i:]
                break

    def handle_data(self, data):
        self.stack[-1].children.append(data)


def text(node):
    return "\n".join(line.strip() for line in node.text().splitlines() if line.strip())


def codes(value):
    # Only a glossary lookup. Raw text, including exclusions, stays authoritative.
    return list(dict.fromkeys(re.sub(r"\s", "", m.group()) for m in re.finditer(
        r"(?<![\d.])\d{4}(?:[ \t]\d{1,3})*(?![\d.])", value)))


def parse_group(group_id, html, meanings):
    if group_id not in PILOT_IDS:
        raise ValueError("Unsupported product group")
    page = Page(html)
    scope = next((n for n in page.root.nodes() if n.tag == "section" and n.has_class("section-content")), None)
    if scope is None:
        raise ValueError("Товарный раздел отсутствует")
    heading = next((text(n) for n in scope.nodes() if n.has_class("section-tab-content__item-h2")
                    and HEADINGS[group_id].casefold() in text(n).casefold()), None)
    tables = [n for n in scope.nodes() if n.has_class("milk-marks-table")]
    if not heading or len(tables) != 1:
        raise ValueError("Не распознан заголовок или таблица перечня")
    table = next((n for n in tables[0].nodes() if n.tag == "table"), None)
    if table is None:
        raise ValueError("Нет товарной таблицы")
    rows, section, spans = [], "", {}
    columns = 2 if group_id == "homeware" else 3
    for tr in (n for n in table.nodes() if n.tag == "tr"):
        cells = [n for n in tr.children if isinstance(n, Node) and n.tag in ("td", "th")]
        if not cells:
            continue
        if len(cells) == 1 and int(cells[0].attrs.get("colspan", "1")) == columns:
            section = text(cells[0])
            continue
        expanded, pending = [], dict(spans)
        spans.clear()
        col = 0
        for cell in cells:
            while col in pending:
                value, remaining = pending.pop(col)
                expanded.append(value)
                if remaining > 1:
                    spans[col] = (value, remaining - 1)
                col += 1
            value = text(cell)
            for _ in range(int(cell.attrs.get("colspan", "1"))):
                expanded.append(value)
                height = int(cell.attrs.get("rowspan", "1"))
                if height > 1:
                    spans[col] = (value, height - 1)
                col += 1
        while col in pending:
            value, remaining = pending.pop(col)
            expanded.append(value)
            if remaining > 1:
                spans[col] = (value, remaining - 1)
            col += 1
        if any("Код ТН" in cell for cell in expanded):
            continue
        if group_id == "cosmetics" and len(expanded) == columns and "этап" in expanded[0] and not any(expanded[1:]):
            section = expanded[0]
            continue
        if len(expanded) != columns or not codes(expanded[0]) or not expanded[-1]:
            raise ValueError("Изменилась структура товарной строки")
        rows.append({"section": section, "sourceName": expanded[-1], "tnvedText": expanded[0],
                     "okpd2Text": expanded[1] if columns == 3 else "", "conditions": "",
                     "meanings": [meanings[c] for c in codes(expanded[0]) if c in meanings]})
    if not rows:
        raise ValueError("Товарная таблица пуста")
    notes_scope = scope if group_id == "children" else tables[0]
    notes = [text(n) for n in notes_scope.nodes() if n.has_class("milk-marks-table__text")]
    conditions = "\n\n".join(notes)
    examples, category = [], ""
    for node in scope.nodes():
        if node.has_class("marking-retractable-block__name"):
            category = text(node)
        elif node.has_class("product-list__item"):
            example = f"{category}: {text(node)}" if category else text(node)
            if example not in examples:
                examples.append(example)
    if group_id == "cosmetics" and ("регистрационное удостоверение" not in conditions or "одновременно" not in conditions or not examples):
        raise ValueError("Не найдены примечания или бытовые примеры косметики")
    if group_id == "grocery" and ("Исключения" not in conditions or "одновременно" not in conditions or conditions.count("—") < 4):
        raise ValueError("Не найдены условия и исключения бакалеи")
    if group_id == "children" and any(part not in conditions for part in ("кодом ТН ВЭД", "кодом ОКПД 2", "воздушных шаров", "велосипедов трехколесных", "азартных игр", "ремесленников", "приложении 1")):
        raise ValueError("Не найдены исключения для детских игрушек")
    return {"sourceHeading": heading, "conditions": conditions, "examples": examples, "rows": rows}


def digest(value):
    raw = json.dumps(value, ensure_ascii=False, sort_keys=True, separators=(",", ":")).encode("utf-8")
    return hashlib.sha256(raw).hexdigest()


def fetch_page(url):
    parts = urlsplit(url)
    host = (parts.hostname or "").encode("idna").decode("ascii")
    if parts.scheme != "https" or host != CHZ_HOST or not parts.path.startswith("/business/projects/"):
        raise ValueError("Разрешены только публичные товарные страницы ЧЗ")
    url = urlunsplit(("https", host, parts.path, parts.query, ""))
    with urlopen(Request(url, headers={"User-Agent": "MarkingCalendar-Products/1.0"}), timeout=30) as response:
        final = urlsplit(response.url)
        if final.scheme != "https" or final.hostname != CHZ_HOST:
            raise ValueError("Источник перенаправил на другой сайт")
        raw = response.read(3 * 1024 * 1024 + 1)
        if len(raw) > 3 * 1024 * 1024:
            raise ValueError("Товарная страница превышает 3 MiB")
        return raw.decode("utf-8")


def check(data_dir, dry_run=False):
    directory = Path(data_dir)
    path = directory / "products.json"
    previous = json.loads(path.read_text(encoding="utf-8-sig")) if path.exists() else None
    if previous is not None and (previous.get("schemaVersion") != 1 or not isinstance(previous.get("groups"), list)):
        raise ValueError("Предыдущий справочник повреждён")
    before = {group["id"]: group for group in previous["groups"]} if previous else {}
    meanings_path = ROOT / "assets/products/names.json"
    meanings = json.loads(meanings_path.read_text(encoding="utf-8-sig"))["codes"] if meanings_path.exists() else {}
    group_map = json.loads((ROOT / "assets/groups/groups.json").read_text(encoding="utf-8-sig"))
    metadata = {urlsplit(g["link"]).path.rstrip("/").split("/")[-1]: g for g in group_map["groups"]}
    now = datetime.now(timezone.utc).isoformat()
    changed, unchanged, failed, unknown, result = [], [], [], set(), []
    for group_id in PILOT_IDS:
        source_html = ""
        try:
            meta = metadata[group_id]
            url = meta.get("goodsUrl") or meta["link"].rstrip("/") + ("/marking_goods/" if group_id in ("homeware", "children") else "/mark_goods/")
            if url.startswith("/"):
                url = "https://" + CHZ_HOST + url
            source_html = fetch_page(url)
            content = parse_group(group_id, source_html, meanings)
            source_content = {**content, "rows": [{k: v for k, v in row.items() if k != "meanings"} for row in content["rows"]]}
            revision = digest({"name": meta["name"], "sourceUrl": url, **content})
            old = before.get(group_id)
            is_changed = old is None or old["revision"] != revision
            group = {"id": group_id, "name": meta["name"], "sourceUrl": url,
                     "sourceHash": digest(source_content), "revision": revision,
                     "changedAt": now if is_changed else old["changedAt"], "checkedAt": now, **content}
            result.append(group)
            (changed if is_changed else unchanged).append(group_id)
            for row in content["rows"]:
                unknown.update(c for c in codes(row["tnvedText"]) if c not in meanings)
        except (OSError, ValueError, KeyError) as error:
            failed.append(group_id)
            print(f"{group_id}: {error}", file=sys.stderr)
            if not dry_run and source_html:
                page = Page(source_html)
                fragment = next((text(n) for n in page.root.nodes() if n.has_class("section-content")), "Товарный раздел не найден")
                rejected = directory / "rejected-products"
                rejected.mkdir(parents=True, exist_ok=True)
                (rejected / f"{group_id}.txt").write_text(f"{error}\n{fragment[:30000]}\n", encoding="utf-8")
            if group_id in before:
                result.append(before[group_id])
    print("PRODUCTS_CHANGED=" + ",".join(changed))
    print("PRODUCTS_UNCHANGED=" + ",".join(unchanged))
    print("PRODUCTS_FAILED=" + ",".join(failed))
    print("UNKNOWN_CODES=" + ",".join(sorted(unknown)))
    if not dry_run and len(result) == len(PILOT_IDS):
        catalog = {"schemaVersion": 1, "revision": digest([(g["id"], g["revision"]) for g in result]), "groups": result}
        directory.mkdir(parents=True, exist_ok=True)
        temporary = path.with_suffix(".json.tmp")
        try:
            temporary.write_text(json.dumps(catalog, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
            os.replace(temporary, path)
        finally:
            temporary.unlink(missing_ok=True)
    return 2 if failed else 0


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("command", choices=["check"])
    parser.add_argument("--data", required=True)
    parser.add_argument("--dry-run", action="store_true")
    args = parser.parse_args()
    try:
        return check(args.data, args.dry_run)
    except (OSError, ValueError, KeyError) as error:
        print(str(error), file=sys.stderr)
        return 4


if __name__ == "__main__":
    sys.exit(main())
