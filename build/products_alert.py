"""Notify the existing maintainer Telegram chat when a product check fails."""
import json
import os
from pathlib import Path
import re
import sys
from urllib.parse import urlencode
from urllib.request import Request, urlopen


def messages(log, groups, run_url):
    names = {g["link"].rstrip("/").split("/")[-1]: g["name"] for g in groups}
    match = re.search(r"^PRODUCTS_FAILED=(.*)$", log, re.MULTILINE)
    failed = match.group(1).strip().split(",") if match and match.group(1).strip() else []
    lines = ["Календарь маркировки: ошибка проверки товарных перечней."]
    for group_id in failed:
        reason = re.search(r"^" + re.escape(group_id) + r": (.+)$", log, re.MULTILINE)
        lines.append(f"• {names.get(group_id, group_id)}: {reason.group(1)[:400] if reason else 'проверка не пройдена'}")
    if not failed:
        lines.append("Сбой задания сбора или публикации. Подробности в журнале запуска.")
    lines.append(run_url)
    result, chunk = [], ""
    for line in lines:
        if len(chunk) + len(line) + 1 > 3500:
            result.append(chunk.rstrip())
            chunk = ""
        chunk += line + "\n"
    if chunk:
        result.append(chunk.rstrip())
    return result


def main():
    token, chat = os.environ.get("TELEGRAM_BOT_TOKEN"), os.environ.get("TELEGRAM_CHAT_ID")
    if not token or not chat:
        print("Не настроены TELEGRAM_BOT_TOKEN / TELEGRAM_CHAT_ID: уведомление не доставлено.", file=sys.stderr)
        return 1
    root = Path(__file__).resolve().parents[1]
    groups = json.loads((root / "assets/groups/groups.json").read_text(encoding="utf-8-sig"))["groups"]
    log_path = root / "products-check.log"
    log = log_path.read_text(encoding="utf-8", errors="replace") if log_path.exists() else ""
    run_url = f"{os.environ['GITHUB_SERVER_URL']}/{os.environ['GITHUB_REPOSITORY']}/actions/runs/{os.environ['GITHUB_RUN_ID']}"
    try:
        for message in messages(log, groups, run_url):
            body = urlencode({"chat_id": chat, "text": message, "disable_web_page_preview": "true"}).encode()
            with urlopen(Request(f"https://api.telegram.org/bot{token}/sendMessage", data=body), timeout=30) as response:
                if not json.load(response).get("ok"):
                    raise ValueError("Telegram rejected message")
    except (OSError, ValueError):
        # Do not print HTTP exception URLs: they contain the bot token.
        print("Не удалось доставить уведомление в Telegram. См. ошибку задания в GitHub Actions.", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
