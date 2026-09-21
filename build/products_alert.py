"""Notify Telegram once per product failure/recovery, acknowledging delivered state."""
import json
import os
from pathlib import Path
import re
import sys
from urllib.parse import urlencode
from urllib.request import Request, urlopen


def current_state(log, groups, previous, exit_code, job_failed):
    results = {}
    for key in ("CHANGED", "UNCHANGED", "FAILED"):
        match = re.search(r"^PRODUCTS_" + key + r"=(.*)$", log, re.MULTILINE)
        if match:
            results[key] = set(filter(None, match.group(1).strip().split(",")))
    expected = {g["link"].rstrip("/").split("/")[-1] for g in groups}
    complete = (exit_code in ("0", "2") and len(results) == 3
                and set().union(*results.values()) == expected
                and bool(results["FAILED"]) == (exit_code == "2"))
    # Missing/partial diagnostics cannot prove that previously failing groups recovered.
    return {"failedGroups": sorted(results["FAILED"] if complete else previous["failedGroups"]),
            "jobFailed": job_failed or not complete}


def messages(log, groups, run_url, previous, current):
    names = {g["link"].rstrip("/").split("/")[-1]: g["name"] for g in groups}
    failed = set(current["failedGroups"]) - set(previous["failedGroups"])
    recovered = set(previous["failedGroups"]) - set(current["failedGroups"])
    if not failed and not recovered and current["jobFailed"] == previous["jobFailed"]:
        return []
    lines = ["Календарь маркировки: состояние проверки товарных перечней изменилось."]
    for group_id in sorted(failed):
        reason = re.search(r"^" + re.escape(group_id) + r": (.+)$", log, re.MULTILINE)
        lines.append(f"• {names.get(group_id, group_id)}: {reason.group(1)[:400] if reason else 'проверка не пройдена'}")
    for group_id in sorted(recovered):
        lines.append(f"• Восстановлено: {names.get(group_id, group_id)}")
    if current["jobFailed"] and not previous["jobFailed"]:
        lines.append("Сбой задания сбора или публикации. Подробности в журнале запуска.")
    elif previous["jobFailed"] and not current["jobFailed"]:
        lines.append("Работа задания сбора и публикации восстановлена.")
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


def main(root=None):
    root = root or Path(__file__).resolve().parents[1]
    groups = json.loads((root / "assets/groups/groups.json").read_text(encoding="utf-8-sig"))["groups"]
    log_path = root / "products-check.log"
    log = log_path.read_text(encoding="utf-8", errors="replace") if log_path.exists() else ""
    state_path = root / "data/products-alert-state.json"
    previous = json.loads(state_path.read_text(encoding="utf-8")) if state_path.exists() else {"failedGroups": [], "jobFailed": False}
    current = current_state(log, groups, previous, os.environ.get("PRODUCTS_CHECK_EXIT_CODE"),
                            os.environ.get("PRODUCTS_JOB_RESULT") != "success"
                            and os.environ.get("PRODUCTS_SOURCE_FAILURE_ONLY") != "true")
    run_url = f"{os.environ['GITHUB_SERVER_URL']}/{os.environ['GITHUB_REPOSITORY']}/actions/runs/{os.environ['GITHUB_RUN_ID']}"
    pending = messages(log, groups, run_url, previous, current)
    if not pending:
        print("Состояние проверки не изменилось: уведомление не требуется.")
        return 0
    token, chat = os.environ.get("TELEGRAM_BOT_TOKEN"), os.environ.get("TELEGRAM_CHAT_ID")
    if not token or not chat:
        print("Не настроены TELEGRAM_BOT_TOKEN / TELEGRAM_CHAT_ID: уведомление не доставлено.", file=sys.stderr)
        return 1
    try:
        for message in pending:
            body = urlencode({"chat_id": chat, "text": message, "disable_web_page_preview": "true"}).encode()
            with urlopen(Request(f"https://api.telegram.org/bot{token}/sendMessage", data=body), timeout=30) as response:
                if not json.load(response).get("ok"):
                    raise ValueError("Telegram rejected message")
    except (OSError, ValueError):
        # Do not print HTTP exception URLs: they contain the bot token.
        print("Не удалось доставить уведомление в Telegram. См. ошибку задания в GitHub Actions.", file=sys.stderr)
        return 1
    # The workflow commits this file only after every message has been delivered.
    state_path.write_text(json.dumps(current, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    return 0


if __name__ == "__main__":
    sys.exit(main())
