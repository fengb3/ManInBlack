#!/usr/bin/env python3
"""
PostToolUse hook: 长耗时 Bash 命令完成后弹出 Windows 通知。

匹配规则:
  - dotnet build / test / publish / restore
  - docker build / compose up / push
  - npm install / yarn / pnpm
  - git clone / git fetch

只匹配 Bash 工具调用，exit_code 非 None 表示命令已完成。
"""

import sys, json, subprocess, os, re, time

# --------------- 哪些命令要通知 ---------------
# (pattern, display_name)
TRIGGERS = [
    (r"\bdotnet build\b",       "dotnet build"),
    (r"\bdotnet test\b",        "dotnet test"),
    (r"\bdotnet publish\b",     "dotnet publish"),
    (r"\bdotnet restore\b",     "dotnet restore"),
    (r"\bdotnet run\b",         "dotnet run"),
    (r"\bdocker build\b",       "docker build"),
    (r"\bdocker compose\b",     "docker compose"),
    (r"\bdocker push\b",        "docker push"),
    (r"\bnpm install\b",        "npm install"),
    (r"\bnpm run build\b",      "npm build"),
    (r"\byarn install\b",       "yarn install"),
    (r"\bpnpm install\b",       "pnpm install"),
    (r"\bgit clone\b",          "git clone"),
    (r"\bgit fetch\b",          "git fetch"),
    (r"\bgh pr create\b",       "gh pr create"),
    (r"\bcurl\b",               "curl"),
    (r"\bwget\b",               "wget"),
]


def notify_windows(title: str, message: str) -> bool:
    """通过 PowerShell 调用 Windows Toast 通知。返回是否成功。"""
    ps_script = f'''
[Windows.UI.Notifications.ToastNotificationManager, Windows.UI.Notifications, ContentType = WindowsRuntime] | Out-Null
$template = [Windows.UI.Notifications.ToastNotificationManager]::GetTemplateContent(0)
$texts = $template.GetElementsByTagName("text")
$texts[0].AppendChild($template.CreateTextNode("{title}")) | Out-Null
$texts[1].AppendChild($template.CreateTextNode("{message}")) | Out-Null
$toast = [Windows.UI.Notifications.ToastNotification]::new($template)
[Windows.UI.Notifications.ToastNotificationManager]::CreateToastNotifier("ClaudeCode").Show($toast)
'''
    try:
        subprocess.run(
            ["powershell", "-NoProfile", "-Command", ps_script],
            capture_output=True, text=True, timeout=10,
        )
        return True
    except Exception:
        return False


def main():
    raw = sys.stdin.read()
    try:
        event = json.loads(raw)
    except json.JSONDecodeError:
        print(json.dumps({"decision": "continue"}))
        return

    # 只看 PostToolUse + Bash
    if event.get("event") != "PostToolUse" and event.get("tool_name") != "Bash":
        print(json.dumps({"decision": "continue"}))
        return

    tool_input = event.get("tool_input", {})
    tool_result = event.get("tool_result", {})
    command = tool_input.get("command", "")

    exit_code = tool_result.get("exit_code")

    # 命令可能还在运行（exit_code 不存在）-- 跳过
    if exit_code is None:
        print(json.dumps({"decision": "continue"}))
        return

    # 匹配触发词
    matched_name = None
    for pattern, name in TRIGGERS:
        if re.search(pattern, command):
            matched_name = name
            break

    if not matched_name:
        print(json.dumps({"decision": "continue"}))
        return

    # 构建通知内容
    status = "完成" if exit_code == 0 else f"失败 (exit={exit_code})"

    # 截取命令摘要
    cmd_short = command[:80].replace("\n", " ")
    if len(command) > 80:
        cmd_short += "..."

    title = f"{matched_name} {status}"
    message = cmd_short

    success = notify_windows(title, message)

    if not success:
        # 回退：输出到 stderr（Claude 会话中可见）
        emoji = "OK" if exit_code == 0 else "FAIL"
        print(f"[notify] {emoji} {matched_name} | {cmd_short}", file=sys.stderr)

    print(json.dumps({"decision": "continue"}))


if __name__ == "__main__":
    main()