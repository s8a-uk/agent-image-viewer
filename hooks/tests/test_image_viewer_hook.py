"""Tests for image_viewer_hook.py. Run: python3 -m unittest discover -s hooks/tests"""

import json
import pathlib
import sys
import tempfile
import unittest

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parents[1]))

import image_viewer_hook as hook  # noqa: E402


class TempDirTestCase(unittest.TestCase):
    def setUp(self):
        self._temp = tempfile.TemporaryDirectory()
        self.dir = pathlib.Path(self._temp.name)
        self._work_dir = hook.WORK_DIR
        hook.WORK_DIR = self.dir / "work"

    def tearDown(self):
        hook.WORK_DIR = self._work_dir
        self._temp.cleanup()

    def file(self, name, content=b"pixels"):
        path = self.dir / name
        path.write_bytes(content)
        return path

    def jsonl(self, name, entries):
        path = self.dir / name
        path.write_text("".join(json.dumps(e) + "\n" for e in entries), encoding="utf-8")
        return str(path)


class ClaudeCodeEventTests(TempDirTestCase):
    def payload(self, **overrides):
        data = {
            "hook_event_name": "PostToolUse",
            "session_id": "s-1",
            "tool_use_id": "toolu_1",
            "cwd": str(self.dir),
            "transcript_path": "/t.jsonl",
            "tool_name": "Read",
            "tool_input": {},
        }
        data.update(overrides)
        return data

    def test_a_read_image_becomes_an_event(self):
        image = self.file("shot.png")

        event = hook.parse_event(self.payload(tool_input={"file_path": str(image)}))

        self.assertEqual("claude-code", event.agent)
        self.assertEqual(("s-1", "toolu_1", "/t.jsonl"), (event.session, event.call, event.transcript))
        self.assertEqual([image], event.images)

    def test_reading_other_files_or_missing_images_is_ignored(self):
        for path in (self.file("notes.txt"), self.dir / "missing.png"):
            with self.subTest(path=path.name):
                self.assertIsNone(hook.parse_event(self.payload(tool_input={"file_path": str(path)})))

    def test_an_mcp_tool_gives_its_saved_file_and_inline_images(self):
        saved = self.file("saved.jpg")
        response = {"content": [{"type": "text", "text": "done"}, {"type": "image", "data": "aGk=", "mimeType": "image/webp"}]}

        event = hook.parse_event(self.payload(
            tool_name="mcp__browser__screenshot", tool_input={"filename": "saved.jpg"}, tool_response=response))

        self.assertEqual(saved, event.images[1])
        inline = event.images[0]
        self.assertEqual([inline], event.temps)
        self.assertEqual(".webp", inline.suffix)
        self.assertEqual(b"hi", inline.read_bytes())

    def test_other_tools_and_incomplete_payloads_are_ignored(self):
        image = str(self.file("shot.png"))
        self.assertIsNone(hook.parse_event(self.payload(tool_name="Bash", tool_input={"file_path": image})))
        self.assertIsNone(hook.parse_event(self.payload(session_id="", tool_input={"file_path": image})))


class ClaudeCodeTextTests(TempDirTestCase):
    def transcript(self, *entries):
        return hook.claude_code_turn(self.jsonl("t.jsonl", entries))

    @staticmethod
    def assistant(uuid, *content):
        return {"type": "assistant", "uuid": uuid, "message": {"content": list(content)}}

    @staticmethod
    def user(uuid, content):
        return {"type": "user", "uuid": uuid, "message": {"content": content}}

    def test_the_text_right_before_the_call_is_the_card_text_and_its_entry_the_group(self):
        blocks = self.transcript(
            self.user("u0", "old prompt"),
            self.assistant("a0", {"type": "text", "text": "Earlier turn."}),
            self.user("u1", "take a screenshot"),
            self.assistant("a1", {"type": "text", "text": "Taking a look."}),
            self.assistant("a2", {"type": "text", "text": "Here it is:"}, {"type": "tool_use", "id": "toolu_1"}),
        )

        self.assertEqual(("Taking a look.\n\nHere it is:", "a2"), hook.claude_code_text(blocks, "toolu_1"))

    def test_text_before_earlier_tool_calls_is_used_until_the_limit(self):
        calls = [self.assistant(f"c{i}", {"type": "tool_use", "id": f"other{i}"}) for i in range(hook.MAX_TOOLS_BACK)]
        near = self.transcript(
            self.assistant("a1", {"type": "text", "text": "Checking."}), *calls,
            self.assistant("a2", {"type": "tool_use", "id": "toolu_1"}))
        far = self.transcript(
            self.assistant("a1", {"type": "text", "text": "Checking."}), *calls,
            self.assistant("c-extra", {"type": "tool_use", "id": "extra"}),
            self.assistant("a2", {"type": "tool_use", "id": "toolu_1"}))

        self.assertEqual(("Checking.", "a1"), hook.claude_code_text(near, "toolu_1"))
        self.assertEqual(("", ""), hook.claude_code_text(far, "toolu_1"))

    def test_a_call_not_written_yet_gives_none(self):
        blocks = self.transcript(self.assistant("a1", {"type": "text", "text": "Hi."}))

        self.assertIsNone(hook.claude_code_text(blocks, "toolu_1"))

    def test_tool_results_do_not_start_a_new_turn(self):
        blocks = self.transcript(
            self.assistant("a1", {"type": "text", "text": "Comparing."}, {"type": "tool_use", "id": "first"}),
            self.user("r1", [{"type": "tool_result", "tool_use_id": "first"}]),
            self.assistant("a2", {"type": "tool_use", "id": "toolu_1"}),
        )

        self.assertEqual(("Comparing.", "a1"), hook.claude_code_text(blocks, "toolu_1"))


class AntigravityTests(TempDirTestCase):
    def payload(self, path, **overrides):
        data = {
            "hookEventName": "PostToolUse",
            "conversationId": "conv-1",
            "cwd": str(self.dir),
            "transcriptPath": "/brain/t.jsonl",
            "stepIdx": 5,
            "error": "",
            "toolCall": {"id": "call_1", "name": "read_file", "args": {"Path": str(path)}},
        }
        data.update(overrides)
        return data

    def test_a_read_image_becomes_an_event(self):
        image = self.file("shot.webp")

        event = hook.parse_event(self.payload(image))

        self.assertEqual("antigravity", event.agent)
        self.assertEqual(("conv-1", "5", str(image)), (event.session, event.call, event.path))
        self.assertEqual([image], event.images)

    def test_failed_calls_other_tools_and_other_files_are_ignored(self):
        image = self.file("shot.png")
        self.assertIsNone(hook.parse_event(self.payload(image, error="exit status 1")))
        self.assertIsNone(hook.parse_event(self.payload(image, toolCall={"name": "run_command", "args": {"Path": str(image)}})))
        self.assertIsNone(hook.parse_event(self.payload(self.file("notes.md"))))

    def test_the_planner_step_that_called_the_tool_gives_text_and_group(self):
        steps = [
            {"step_index": 1, "type": "PLANNER_RESPONSE", "content": "Old.", "tool_calls": [{"name": "read_file", "args": {"Path": "/a.png"}}]},
            {"step_index": 3, "type": "PLANNER_RESPONSE", "content": "Checking both. ",
             "tool_calls": [{"name": "read_file", "args": {"Path": "/a.png"}}, {"name": "read_file", "args": {"Path": "/b.png"}}]},
            {"step_index": 4, "type": "GENERIC", "content": "Showing binary file"},
            {"step_index": 9, "type": "PLANNER_RESPONSE", "content": "Later.", "tool_calls": [{"name": "read_file", "args": {"Path": "/a.png"}}]},
        ]

        self.assertEqual(("Checking both.", "conv-1:3"), hook.antigravity_text(steps, "5", "/a.png", "conv-1"))
        self.assertEqual(("Checking both.", "conv-1:3"), hook.antigravity_text(steps, "5", "/b.png", "conv-1"))
        self.assertIsNone(hook.antigravity_text(steps, "5", "/c.png", "conv-1"))


class PostArgsTests(unittest.TestCase):
    def event(self):
        return hook.ToolEvent("claude-code", "s-1", "/home/me/project", "/t.jsonl", "toolu_1")

    def test_all_parts_are_passed(self):
        args = hook.post_args(self.event(), r"C:\t\text.txt", [r"C:\t\a.png", r"C:\t\b.png"], "g-1", "My session")

        self.assertEqual(
            ["post", "--session", "s-1", "--cwd", "/home/me/project", "--session-name", "My session",
             "--group", "g-1", "--text-file", r"C:\t\text.txt", r"C:\t\a.png", r"C:\t\b.png"],
            args)

    def test_empty_group_and_name_are_left_out(self):
        args = hook.post_args(self.event(), r"C:\t\text.txt", [r"C:\t\a.png"], "", "")

        self.assertNotIn("--group", args)
        self.assertNotIn("--session-name", args)


class UnknownInputTests(unittest.TestCase):
    def test_a_payload_from_another_agent_is_ignored(self):
        self.assertIsNone(hook.parse_event({"event": "PostToolUse", "tool": "view"}))


if __name__ == "__main__":
    unittest.main()
