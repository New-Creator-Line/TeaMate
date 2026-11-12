"""
Unit test suite for Linear & Raycast Obsidian Pro Theme deliverables.
Validates DOM IDs, classes, contract compliance, CSS properties, zero-emojis, and JS functions.
"""

import os
import re
import unittest

BASE_DIR = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
HTML_PATH = os.path.join(BASE_DIR, "index-obsidian.html")
CSS_PATH = os.path.join(BASE_DIR, "themes", "theme-obsidian.css")


class TestObsidianThemeDeliverables(unittest.TestCase):
    def setUp(self):
        self.assertTrue(os.path.exists(HTML_PATH), f"HTML file missing: {HTML_PATH}")
        self.assertTrue(os.path.exists(CSS_PATH), f"CSS file missing: {CSS_PATH}")
        with open(HTML_PATH, "r", encoding="utf-8") as f:
            self.html = f.read()
        with open(CSS_PATH, "r", encoding="utf-8") as f:
            self.css = f.read()

    def test_zero_emojis(self):
        emoji_pattern = re.compile(
            r"[\U00010000-\U0010ffff]|[\u2600-\u27bf]|[\u2300-\u23ff]|[\u2b50-\u2b55]|[\u203c-\u2049]"
        )
        html_emojis = emoji_pattern.findall(self.html)
        css_emojis = emoji_pattern.findall(self.css)
        self.assertEqual(len(html_emojis), 0, f"Found emojis in HTML: {html_emojis}")
        self.assertEqual(len(css_emojis), 0, f"Found emojis in CSS: {css_emojis}")

    def test_lucide_cdn_and_icons(self):
        self.assertIn("https://unpkg.com/lucide@latest", self.html)
        self.assertIn("lucide.createIcons", self.html)
        lucide_matches = re.findall(r'data-lucide=["\']([a-zA-Z0-9_-]+)["\']', self.html)
        self.assertGreater(len(lucide_matches), 15, "Expected numerous Lucide icon tags in HTML")
        for expected_icon in ["printer", "user", "calendar", "zap", "bar-chart-2", "mic", "paperclip", "send"]:
            self.assertIn(expected_icon, lucide_matches, f"Missing icon: {expected_icon}")

    def test_required_dom_ids(self):
        required_ids = [
            "calendar-toggle-btn",
            "calendar-today-badge",
            "calendar-backdrop",
            "calendar-drawer",
            "scope-my-btn",
            "scope-all-btn",
            "scope-lock-icon",
            "current-month-label",
            "calendar-days-grid",
            "selected-day-title",
            "day-meetings-list",
            "chat-window",
            "user-select",
            "role-badge",
            "user-input",
            "send-btn",
            "attach-btn",
            "mic-btn",
            "audio-file-input",
            "voice-bar",
            "voice-cancel-btn",
            "voice-done-btn",
            "voice-timer",
            "record-timer",
            "voice-waveform",
            "stt-status-badge"
        ]
        for dom_id in required_ids:
            self.assertIn(f'id="{dom_id}"', self.html, f"Missing DOM id: {dom_id}")

    def test_required_classes_and_elements(self):
        required_classes = [
            "kbd-chip",
            "chat-duration-selector",
            "chat-slots-container",
            "slot-chip",
            "meeting-confirm-card",
            "btn-confirm-yes",
            "btn-confirm-no",
            "confirm-success-banner",
            "notification-stub-badge",
            "day-dots",
            "dot-green",
            "dot-amber",
            "dot-red",
            "mom-card",
            "btn-bitrix",
            "btn-docx-download"
        ]
        for cls in required_classes:
            self.assertTrue(cls in self.html or cls in self.css, f"Missing class: {cls}")

    def test_css_palette_and_surfaces(self):
        expected_styles = [
            "#08090c",  # Matte graphite base
            "#0f1015",  # Base surface
            "#6366f1",  # Indigo accent
            "#10b981",  # Emerald accent
            "#f59e0b",  # Amber accent
            "rgba(255, 255, 255, 0.07)",  # Subpixel border
            ".kbd-chip",
            "#calendar-drawer",
            ".calendar-drawer",
            ".day-dots",
            ".dot-green",
            ".dot-amber",
            ".dot-red",
            ".slot-chip",
            ".meeting-confirm-card",
            ".btn-confirm-yes",
            ".btn-confirm-no",
            "#voice-waveform"
        ]
        for style_token in expected_styles:
            self.assertIn(style_token, self.css, f"CSS missing token: {style_token}")

    def test_required_js_functions(self):
        required_funcs = [
            "toggleCalendarDrawer",
            "setCalendarScope",
            "changeMonth",
            "refreshCalendarEvents",
            "renderCalendarDaysGrid",
            "selectCalendarDay",
            "renderDayMeetings",
            "renderActionCard",
            "showStatelessMeetingConfirmCard",
            "submitMeetingConfirmation",
            "cancelMeetingCard",
            "handleSend",
            "toggleRecording",
            "startRecording",
            "cancelRecording",
            "stopRecordingAndSend",
            "handleAudioUpload",
            "renderMeetingProtocolCard",
            "createBitrixTask",
            "downloadDocx",
            "loadUsers",
            "onUserChange"
        ]
        for func_name in required_funcs:
            self.assertIn(f"function {func_name}", self.html, f"Missing JS function: {func_name}")

    def test_stateless_calendar_endpoints(self):
        self.assertIn("/api/calendar/events", self.html)
        self.assertIn("/api/calendar/confirm", self.html)
        self.assertIn("X-Telegram-Id", self.html)
        self.assertIn("scheduled", self.html)


if __name__ == "__main__":
    unittest.main()
