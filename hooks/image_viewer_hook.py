#!/usr/bin/env python3
"""Coding-agent hook that shows the images the agent looks at in AgentImageViewer.

Install it as a PostToolUse hook (see the README). It reads the hook's JSON from stdin and
understands two agents, told apart by the shape of that JSON:

  Claude Code      images read with Read, and images MCP tools return or save
  Antigravity CLI  images read with read_file

Each image is copied right away, then a detached child waits until the agent has written the
tool call to its transcript, takes the text the agent wrote just before it, and posts images and
text to the viewer as one card. The hook itself returns at once and never fails the agent:
every error is swallowed and it exits 0.

Runs in WSL; the viewer is the Windows exe, reached through WSL interop.
"""

from __future__ import annotations

import base64
import glob
import json
import os
import pathlib
import shutil
import subprocess
import sys
import time
import uuid
from dataclasses import dataclass, field

IMAGE_EXTS = {".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".tif", ".tiff"}
MIME_EXTS = {"image/png": ".png", "image/jpeg": ".jpg", "image/gif": ".gif", "image/webp": ".webp", "image/bmp": ".bmp"}
# Keys in an MCP tool's input that commonly hold the path of an image it saved (e.g. screenshots).
PATH_KEYS = {"file_path", "path", "filename", "filePath", "output", "outputPath", "savePath"}

EXE_ENV = "AGENT_IMAGE_VIEWER_EXE"
EXE_GLOB = "/mnt/c/Users/*/AppData/Local/Programs/AgentImageViewer/AgentImageViewer.exe"
WORK_DIR = pathlib.Path(os.environ.get("XDG_CACHE_HOME", "~/.cache")).expanduser() / "agent-image-viewer"
CLAUDE_SESSIONS_DIR = pathlib.Path(os.environ.get("CLAUDE_CONFIG_DIR", "~/.claude")).expanduser() / "sessions"

POST_TIMEOUT_SEC = 20
# How long the child waits for the agent to write the image's tool call to the transcript.
FLUSH_WAIT_SEC = 10
FLUSH_POLL_SEC = 0.05
# How many tool calls back from the image's own call to look for the card text (Claude Code).
MAX_TOOLS_BACK = 10


@dataclass
class ToolEvent:
    """What the hook needs from one PostToolUse call, whichever agent sent it."""

    agent: str  # "claude-code" or "antigravity"
    session: str
    cwd: str
    transcript: str
    # Identifies the tool call in the transcript: the tool_use id (Claude Code) or the step index (Antigravity).
    call: str
    images: list[pathlib.Path] = field(default_factory=list)
    # Images the hook itself saved (inline MCP images); deleted once posted.
    temps: list[pathlib.Path] = field(default_factory=list)
    # Antigravity: the image path the tool call names, to find the call in the transcript.
    path: str = ""


# --- Reading the hook input -------------------------------------------------------------------


def parse_event(data: dict) -> ToolEvent | None:
    """The tool call the hook was run for, or None when it involves no image."""
    if "hook_event_name" in data:
        return _claude_code_event(data)
    if "hookEventName" in data:
        return _antigravity_event(data)
    return None


def _claude_code_event(data: dict) -> ToolEvent | None:
    if not data.get("session_id") or not data.get("tool_use_id"):
        return None

    tool_name = data.get("tool_name") or ""
    tool_input = data.get("tool_input") or {}
    cwd = pathlib.Path(data.get("cwd") or ".")
    event = ToolEvent("claude-code", data["session_id"], data.get("cwd") or "", data.get("transcript_path") or "", data["tool_use_id"])

    if tool_name == "Read":
        path = pathlib.Path(tool_input.get("file_path") or "")
        if is_image_file(path):
            event.images.append(path)
    elif tool_name.startswith("mcp__"):
        extract_inline_images(data.get("tool_response"), event.temps)
        event.images.extend(event.temps)
        for key, value in tool_input.items():
            if key in PATH_KEYS and isinstance(value, str) and value:
                path = pathlib.Path(value)
                path = path if path.is_absolute() else cwd / path
                if is_image_file(path):
                    event.images.append(path)

    return event if event.images else None


def _antigravity_event(data: dict) -> ToolEvent | None:
    call = data.get("toolCall") or {}
    path = (call.get("args") or {}).get("Path") or ""
    if data.get("error") or call.get("name") != "read_file" or not data.get("conversationId"):
        return None
    if not is_image_file(pathlib.Path(path)):
        return None
    return ToolEvent(
        "antigravity",
        data["conversationId"],
        data.get("cwd") or "",
        data.get("transcriptPath") or "",
        str(data.get("stepIdx", "")),
        images=[pathlib.Path(path)],
        path=path,
    )


def is_image_file(path: pathlib.Path) -> bool:
    return path.suffix.lower() in IMAGE_EXTS and path.is_file()


def extract_inline_images(node, found: list[pathlib.Path]) -> None:
    """Saves the base64 image blocks of a tool response (MCP or Anthropic style)."""
    if isinstance(node, dict):
        if node.get("type") == "image":
            source = node.get("source") if isinstance(node.get("source"), dict) else node
            data = source.get("data")
            mime = source.get("mimeType") or source.get("media_type") or "image/png"
            if isinstance(data, str) and data:
                try:
                    WORK_DIR.mkdir(parents=True, exist_ok=True)
                    path = WORK_DIR / f"{uuid.uuid4().hex}{MIME_EXTS.get(mime, '.png')}"
                    path.write_bytes(base64.b64decode(data))
                    found.append(path)
                except (ValueError, OSError):
                    pass
            return
        for value in node.values():
            extract_inline_images(value, found)
    elif isinstance(node, list):
        for value in node:
            extract_inline_images(value, found)


# --- Finding the card text in the transcript --------------------------------------------------


def claude_code_turn(transcript: str) -> list[tuple[str, str, str]]:
    """(kind, value, entry uuid) for each block of the current turn, in order.

    kind is "text" (value: the text), "tool_use" or "tool_result" (value: the tool_use id).
    """
    blocks: list[tuple[str, str, str]] = []
    for entry in read_jsonl(transcript):
        content = (entry.get("message") or {}).get("content")
        if not isinstance(content, list):
            if entry.get("type") == "user":
                blocks = []
            continue
        content = [c for c in content if isinstance(c, dict)]
        if entry.get("type") == "user":
            results = [c.get("tool_use_id") for c in content if c.get("type") == "tool_result"]
            if not results:
                blocks = []  # a real user prompt starts a new turn
            blocks += [("tool_result", r, entry.get("uuid") or "") for r in results]
        elif entry.get("type") == "assistant":
            for c in content:
                if c.get("type") == "text":
                    blocks.append(("text", c.get("text") or "", entry.get("uuid") or ""))
                elif c.get("type") == "tool_use":
                    blocks.append(("tool_use", c.get("id") or "", entry.get("uuid") or ""))
    return blocks


def claude_code_text(blocks: list[tuple[str, str, str]], call: str) -> tuple[str, str] | None:
    """(text, group) for the tool_use `call`, or None while the call is not in the transcript yet.

    The text is the run of assistant text right before the call, at most MAX_TOOLS_BACK other calls
    back; the group is the uuid of the entry holding it, so images read under one message share a card.
    """
    end = next((i for i, (kind, value, _) in enumerate(blocks) if kind == "tool_use" and value == call), None)
    if end is None:
        return None
    tools_back, texts, group = 0, [], ""
    for kind, value, entry in reversed(blocks[:end]):
        if kind == "text":
            if value.strip():
                texts.append(value.strip())
                group = group or entry
            continue
        if texts:
            break
        if kind == "tool_use":
            tools_back += 1
            if tools_back > MAX_TOOLS_BACK:
                return "", ""
    return "\n\n".join(reversed(texts)), group


def antigravity_text(steps: list[dict], step: str, path: str, session: str) -> tuple[str, str] | None:
    """(text, group) for the read_file of `path` in step `step`, or None while it is not written yet.

    The model's text and its tool calls are one PLANNER_RESPONSE step before the tool's own step, so
    that step is both the text and, with the session, the group.
    """
    try:
        before = int(step)
    except ValueError:
        before = sys.maxsize
    planner = None
    for entry in steps:
        index = entry.get("step_index")
        if not isinstance(index, int) or index >= before or entry.get("type") != "PLANNER_RESPONSE":
            continue
        if any((c.get("args") or {}).get("Path") == path for c in entry.get("tool_calls") or [] if isinstance(c, dict)):
            planner = entry
    if planner is None:
        return None
    return (planner.get("content") or "").strip(), f"{session}:{planner['step_index']}"


def read_jsonl(path: str) -> list[dict]:
    entries = []
    try:
        with open(path, encoding="utf-8") as f:
            for line in f:
                try:
                    entry = json.loads(line)
                except json.JSONDecodeError:
                    continue
                if isinstance(entry, dict):
                    entries.append(entry)
    except OSError:
        pass
    return entries


def card_text(event: ToolEvent) -> tuple[str, str]:
    """Waits until the agent has written the tool call, then returns (text, group); ("", "") if it never does."""
    deadline = time.monotonic() + FLUSH_WAIT_SEC
    while True:
        if event.agent == "claude-code":
            found = claude_code_text(claude_code_turn(event.transcript), event.call)
        else:
            found = antigravity_text(read_jsonl(event.transcript), event.call, event.path, event.session)
        if found is not None:
            return found
        if time.monotonic() >= deadline:
            return "", ""
        time.sleep(FLUSH_POLL_SEC)


# --- Talking to the viewer --------------------------------------------------------------------


def claude_code_session_name(session: str) -> str:
    """The name /list-agents shows: the newest ~/.claude/sessions/*.json entry for this session."""
    best, best_updated = "", -1
    for path in CLAUDE_SESSIONS_DIR.glob("*.json"):
        try:
            entry = json.loads(path.read_text(encoding="utf-8"))
        except (OSError, ValueError):
            continue
        if not isinstance(entry, dict) or entry.get("sessionId") != session:
            continue
        updated = entry.get("updatedAt") or 0
        name = entry.get("name")
        if isinstance(name, str) and name and updated > best_updated:
            best, best_updated = name, updated
    return best


def post_args(event: ToolEvent, text_file: str, images: list[str], group: str, session_name: str) -> list[str]:
    """Arguments of `post`. Paths must already be Windows paths."""
    args = ["post", "--session", event.session, "--cwd", event.cwd]
    if session_name:
        args += ["--session-name", session_name]
    # Without a group the viewer never merges the card with another.
    if group:
        args += ["--group", group]
    return [*args, "--text-file", text_file, *images]


def find_exe() -> str | None:
    exe = os.environ.get(EXE_ENV)
    if exe:
        return exe
    hits = glob.glob(EXE_GLOB)
    return hits[0] if hits else None


def to_windows_path(path: pathlib.Path | str) -> str:
    return subprocess.check_output(["wslpath", "-w", str(path)], text=True).strip()


# --- Entry points -----------------------------------------------------------------------------


def snapshot(event: ToolEvent) -> list[pathlib.Path]:
    """Copies of the images, so a re-capture or deletion before the child posts is harmless."""
    WORK_DIR.mkdir(parents=True, exist_ok=True)
    copies = list(event.temps)  # already saved there
    for path in event.images:
        if path in event.temps:
            continue
        copy = WORK_DIR / f"{uuid.uuid4().hex}{path.suffix.lower()}"
        try:
            shutil.copyfile(path, copy)
        except OSError:
            continue
        copies.append(copy)
    return copies


def on_tool_use(data: dict) -> None:
    event = parse_event(data)
    if event is None or not find_exe():
        return
    copies = snapshot(event)
    if not copies:
        return
    # The agent writes this tool call (and the text before it) to the transcript only after the
    # hook returns, so a detached child waits for it and then posts the card.
    payload = {"event": {**event.__dict__, "images": [str(c) for c in copies], "temps": []}}
    try:
        child = subprocess.Popen(
            [sys.executable, os.path.abspath(__file__), "--post"],
            stdin=subprocess.PIPE,
            stdout=subprocess.DEVNULL,
            stderr=subprocess.DEVNULL,
            start_new_session=True,
        )
        child.stdin.write(json.dumps(payload).encode("utf-8"))
        child.stdin.close()
    except Exception:
        for copy in copies:
            copy.unlink(missing_ok=True)
        raise


def post(data: dict) -> None:
    event = ToolEvent(**data["event"])
    images = [pathlib.Path(i) for i in event.images]
    text_file = WORK_DIR / f"{uuid.uuid4().hex}.txt"
    try:
        exe = find_exe()
        if not exe:
            return
        text, group = card_text(event)
        text_file.write_text(text, encoding="utf-8")
        session_name = claude_code_session_name(event.session) if event.agent == "claude-code" else ""
        args = post_args(event, to_windows_path(text_file), [to_windows_path(i) for i in images], group, session_name)
        subprocess.run([exe, *args], timeout=POST_TIMEOUT_SEC, stdin=subprocess.DEVNULL,
                       stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, check=False)
    finally:
        text_file.unlink(missing_ok=True)
        for image in images:
            image.unlink(missing_ok=True)


def main() -> None:
    try:
        data = json.load(sys.stdin)
        if len(sys.argv) > 1 and sys.argv[1] == "--post":
            post(data)
        else:
            on_tool_use(data)
    except Exception:  # noqa: BLE001 - a broken viewer must never break the agent
        pass


if __name__ == "__main__":
    main()
    sys.exit(0)
