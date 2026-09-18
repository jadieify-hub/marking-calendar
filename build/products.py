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
    found = (re.sub(r"\s", "", m.group()) for m in re.finditer(
        r"(?<![\d.])\d{4,10}(?:[ \t]\d{1,6})*(?![\d.])", value))
    return list(dict.fromkeys(code for code in found if len(code) <= 10))


def matching(node, class_name):
    return [n for n in node.nodes() if n.has_class(class_name)]


def first_text(node, class_name):
    found = matching(node, class_name)
    return text(found[0]) if found else ""


def field_kind(label):
    normalized = re.sub(r"[\s-]", "", label).casefold()
    if "наименование" in normalized or "определение" in normalized or normalized == "видпродукции":
        if not normalized.startswith("код") and not normalized.startswith("номер"):
            return "sourceName"
    if "тнвэд" in normalized:
        return "tnvedText"
    if "окпд" in normalized:
        return "okpd2Text"
    return "conditions"


def make_row(values, section, meanings):
    row = {"section": section, "sourceName": "", "tnvedText": "", "okpd2Text": "", "conditions": "", **values}
    if not row["sourceName"] or not (codes(row["tnvedText"]) or re.search(r"\d{2}\.\d{2}", row["okpd2Text"])):
        raise ValueError("Изменилась структура товара: нет наименования или кодов")
    row["meanings"] = [meanings[c] for c in codes(row["tnvedText"]) if c in meanings]
    return row


def table_rows(table):
    # Several official pages put header cells directly into tbody/thead.
    for node in table.nodes():
        if node.tag in ("table", "thead", "tbody", "tr"):
            cells = [n for n in node.children if isinstance(n, Node) and n.tag in ("td", "th")]
            if cells:
                yield cells


def parse_table(table, heading, section, meanings):
    rows, notes, spans, headers = [], [], {}, []
    for cells in table_rows(table):
        raw = [text(c) for c in cells]
        if raw[0] and not any(raw[1:]) and re.match(r"(?:[IVX\d]+\s*этап|Этап|С \d|Исключения|Примечани)", raw[0], re.I):
            if re.match(r"Исключения|Примечани", raw[0], re.I):
                notes.append(raw[0])
            else:
                section = raw[0]
            continue
        # A colspan heading is a stage/category or a note, never a product.
        if len(cells) == 1 and int(cells[0].attrs.get("colspan", "1")) > 1:
            if re.match(r"(?:Исключения|Примечани)", raw[0], re.I):
                notes.append(raw[0])
            else:
                section = raw[0]
            continue
        if len(cells) == 1 and "Перечень ТН ВЭД" in raw[0]:
            heading = raw[0].split("Перечень ТН ВЭД")[0].strip()
            headers = ["ТН ВЭД"]
            continue
        if any(field_kind(v) in ("tnvedText", "okpd2Text") for v in raw) and not any(codes(v) for v in raw):
            headers = raw
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
        while pending:
            if col in pending:
                value, remaining = pending.pop(col)
                expanded.append(value)
                if remaining > 1:
                    spans[col] = (value, remaining - 1)
            else:
                expanded.append("")
            col += 1
        # The veterinary source includes empty cells under a spanning name.
        while headers and len(expanded) > len(headers) and not expanded[-1]:
            expanded.pop()
        if not headers:
            if len(expanded) == 2 and codes(expanded[0]):
                headers = ["ТН ВЭД", "Наименование"]
            else:
                raise ValueError("Не распознаны заголовки товарной таблицы")
        if len(expanded) != len(headers) or pending:
            raise ValueError("Изменилась структура товарной строки")
        if expanded[0] and not any(expanded[1:]) and not codes(expanded[0]):
            section = expanded[0]
            continue
        values = {}
        for label, value in zip(headers, expanded):
            kind = field_kind(label)
            if kind == "conditions" and not value:
                raise ValueError("Исчез дополнительный классификатор: " + label)
            if value:
                if kind == "conditions":
                    value = label + ": " + value
                values[kind] = "\n".join(filter(None, (values.get(kind), value)))
        if not any(field_kind(label) == "sourceName" for label in headers):
            values["sourceName"] = heading
            if len(headers) > 1:
                values["conditions"] = "Источник публикует перечень кодов без отдельных наименований товаров"
        rows.append(make_row(values, section, meanings))
    if not rows:
        raise ValueError("Товарная таблица пуста")
    return rows, notes


def parse_card(card, section, meanings):
    name = first_text(card, "marking-retractable-block__name")
    if not name:
        raise ValueError("Исчезло наименование карточки товара")
    date = first_text(card, "marking-retractable-block__date")
    stages = [line for line in name.splitlines() if re.match(r"[IVX\d]+\s*этап", line, re.I)]
    if stages:
        section = "\n".join(stages)
    values = {"sourceName": name}
    fields = matching(card, "marking-retractable-block__content-row")
    if not fields:
        raise ValueError("Не найдены поля карточки товара")
    for field in fields:
        label = first_text(field, "marking-retractable-block__content-title")
        value = first_text(field, "marking-retractable-block__content-code")
        kind = field_kind(label)
        if kind == "sourceName":
            details = "\n".join(text(n) for n in matching(field, "product-list__item"))
            if not details:
                details = text(field).removeprefix(label).strip()
            normalized_name = re.sub(r"\s+", " ", name).casefold().strip(".;")
            normalized_details = re.sub(r"\s+", " ", details).casefold().strip(".;")
            if details and normalized_details.startswith(normalized_name):
                values["sourceName"] = details
            elif details and normalized_details != normalized_name:
                values["sourceName"] += "\n" + details
        elif kind in ("tnvedText", "okpd2Text"):
            if not value:
                raise ValueError("В карточке исчезли коды: " + label)
            # The water source puts TN VED in the label and OKPD2 in its value.
            if codes(label) and re.fullmatch(r"\d{2}(?:\.\d{2,3})+", value):
                values["tnvedText"] = ", ".join(codes(label))
                values["okpd2Text"] = value
            else:
                values[kind] = value
        else:
            if not value:
                raise ValueError("Исчез дополнительный классификатор: " + label)
            values["conditions"] = "\n".join(filter(None, (values.get("conditions"), text(field))))
    return make_row(values, "\n".join(filter(None, (section, date))), meanings)


def source_notes(scope):
    # Keep the prose around tables/cards, including exclusions and footnotes.
    skip_classes = ("marking-retractable-block", "check-mark-banner", "qa-banner")
    def clean(node):
        if isinstance(node, str):
            return node
        if node.tag in ("table", "script", "style", "svg") or any(node.has_class(c) for c in skip_classes):
            return ""
        if node.tag == "a" and node.has_class("action-btn"):
            return ""
        copied = Node(node.tag)
        copied.children = [clean(child) for child in node.children]
        return copied
    cleaned = clean(scope)
    value = text(cleaned)
    blocks = sum(n.tag in ("p", "ul", "ol", "h1", "h2", "h3") and bool(text(n)) for n in cleaned.nodes())
    return ([value] if value else []), blocks


def parse_group(group_id, html, meanings):
    contracts = json.loads((ROOT / "assets/products/sources.json").read_text(encoding="utf-8"))["groups"]
    contract = contracts.get(group_id)
    if not contract or contract.get("unavailable"):
        raise ValueError("Товарный источник недоступен или не проверен")
    page = Page(html)
    scope = next((n for n in page.root.nodes() if n.has_class("section-content")), None)
    if scope is None:
        raise ValueError("Товарный раздел отсутствует")
    headings = matching(scope, "section-tab-content__item-h2")
    heading = text(headings[0]) if headings else first_text(page.root, "main-banner__title")
    if not heading:
        raise ValueError("Не найден заголовок товарного источника")
    if group_id in HEADINGS and HEADINGS[group_id].casefold() not in heading.casefold():
        raise ValueError("Не распознан заголовок перечня")
    rows, table_notes, section = [], [], ""
    tables = [n for n in scope.nodes() if n.tag == "table"]
    cards = matching(scope, "marking-retractable-block")
    containers = tables if contract["format"] == "table" else cards
    if len(containers) < contract["minContainers"]:
        raise ValueError("Исчезла таблица или карточка товарного перечня")
    if len(matching(scope, "milk-marks-table__text")) < contract.get("minNoteBlocks", 0):
        raise ValueError("Исчезли примечания или этапы товарного перечня")
    for node in scope.nodes():
        if node in containers:
            if node.tag == "table":
                parsed, notes = parse_table(node, heading, section, meanings)
                rows.extend(parsed)
                table_notes.extend(notes)
            else:
                rows.append(parse_card(node, section, meanings))
        elif any(node.has_class(c) for c in ("milk-marks-table__text", "section-tab-content__item-h2", "text-par-p3")):
            value = text(node)
            if re.match(r"[IVX\d]+\s*этап|этап|с \d{1,2} [а-я]+ 20|с \d{2}\.\d{2}\.20", value, re.I):
                section = value
    if len(rows) < contract.get("minRows", 1):
        raise ValueError("Исчезли строки товарного перечня")
    for field in ("tnvedText", "okpd2Text"):
        if sum(bool(row[field]) for row in rows) < contract.get(field, 0):
            raise ValueError("Исчезла часть кодов " + field)
    if len(matching(scope, "marking-retractable-block__content-row")) < contract.get("minCardFields", 0):
        raise ValueError("Исчезли поля карточек товарного перечня")
    notes, prose_blocks = source_notes(scope)
    if prose_blocks < contract.get("minProseBlocks", 0):
        raise ValueError("Исчез текстовый блок условий товарного перечня")
    notes += table_notes
    if not headings:
        notes.insert(0, heading)
    conditions = "\n\n".join(notes)
    examples, category = [], ""
    for node in scope.nodes():
        if node.has_class("marking-retractable-block__name"):
            category = text(node)
        elif node.has_class("product-list__item") and contract["format"] == "table":
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
    for group_id in metadata:
        source_html = ""
        try:
            meta = metadata[group_id]
            url = meta.get("goodsUrl") or meta["link"]
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
    if not dry_run and result:
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
