import os
import re
import unittest

REPO_ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..', '..', '..'))
INDEX_HTML = os.path.join(REPO_ROOT, 'infra', 'nginx', 'html', 'index.html')
THEMES_DIR = os.path.join(REPO_ROOT, 'infra', 'nginx', 'html', 'themes')

class TestUnifiedIndex(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        with open(INDEX_HTML, 'r', encoding='utf-8') as f:
            cls.html = f.read()

    def test_theme_stylesheets_exist(self):
        for theme in ['theme-amber.css', 'theme-obsidian.css', 'theme-liquid-glass.css']:
            path = os.path.join(THEMES_DIR, theme)
            self.assertTrue(os.path.exists(path), f"Missing {theme}")
            self.assertGreater(os.path.getsize(path), 1000, f"{theme} is too small")

    def test_zero_emojis(self):
        emoji_pattern = re.compile(r'[\U00010000-\U0010ffff]', flags=re.UNICODE)
        matches = emoji_pattern.findall(self.html)
        self.assertEqual(len(matches), 0, f"Found unexpected emojis: {matches}")

    def test_lucide_cdn_and_icons(self):
        self.assertIn('https://unpkg.com/lucide@latest', self.html)
        self.assertIn('data-lucide=', self.html)
        self.assertIn('lucide.createIcons()', self.html)

    def test_mandatory_dom_ids(self):
        required_ids = [
            'prompt-input', 'send-btn', 'attach-audio-btn', 'audio-file-input',
            'voice-toggle-btn', 'voice-bar', 'voice-cancel-btn', 'voice-done-btn',
            'voice-timer', 'voice-waveform', 'user-select', 'role-badge',
            'calendar-toggle-btn', 'calendar-today-badge', 'calendar-drawer',
            'theme-select', 'app-theme-link', 'scope-my-btn', 'scope-all-btn',
            'current-month-label', 'calendar-days-grid', 'selected-day-title',
            'day-meetings-list'
        ]
        for dom_id in required_ids:
            self.assertIn(f'id="{dom_id}"', self.html, f"Missing id: {dom_id}")

    def test_mandatory_classes(self):
        required_classes = [
            'slot-chip', 'meeting-confirm-card', 'chat-duration-selector',
            'chat-slots-container', 'btn-confirm-yes', 'btn-confirm-no',
            'theme-switcher'
        ]
        for cls_name in required_classes:
            self.assertIn(cls_name, self.html, f"Missing class: {cls_name}")

    def test_dynamic_end_date_calculation(self):
        # Must not contain hardcoded -31
        self.assertNotIn("-31`", self.html)
        self.assertNotIn("-31'", self.html)
        self.assertNotIn('-31"', self.html)
        self.assertIn('month + 1, 0', self.html)

    def test_theme_switcher_options(self):
        self.assertIn('value="liquid-glass"', self.html)
        self.assertIn('value="amber"', self.html)
        self.assertIn('value="obsidian"', self.html)
        self.assertIn('switchTheme', self.html)

    def test_voice_input_non_destructive_append(self):
        # Must not overwrite input.value with naked data.text.trim()
        self.assertNotIn("input.value = data.text.trim();", self.html)
        self.assertIn("selectionStart", self.html)
        self.assertIn("recognizedText", self.html)

if __name__ == '__main__':
    unittest.main()
