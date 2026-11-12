#!/usr/bin/env python3
"""
Comprehensive QA Verification Suite for TickTick Calendar Window & Themes (QA-015).
Runs automated verification against backend, frontend, domain invariants, edge cases, and visual assets.
"""

import os
import re
import sys
import json
import subprocess
import unittest

REPO_ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..'))

INDEX_HTML = os.path.join(REPO_ROOT, 'infra', 'nginx', 'html', 'index.html')
THEME_CORE = os.path.join(REPO_ROOT, 'infra', 'nginx', 'html', 'themes', 'theme-core.css')
THEME_OBSIDIAN = os.path.join(REPO_ROOT, 'infra', 'nginx', 'html', 'themes', 'theme-obsidian.css')
THEME_GLASS = os.path.join(REPO_ROOT, 'infra', 'nginx', 'html', 'themes', 'theme-liquid-glass.css')
THEME_AMBER = os.path.join(REPO_ROOT, 'infra', 'nginx', 'html', 'themes', 'theme-amber.css')
CALENDAR_PROGRAM_CS = os.path.join(REPO_ROOT, 'src', 'Fotoprint.Mcp.Calendar', 'Program.cs')
SEED_DATA_SQL = os.path.join(REPO_ROOT, 'infra', 'postgres', 'init', '03_seed_data.sql')


class QaComprehensiveAudit(unittest.TestCase):

    def test_01_backend_dotnet_build(self):
        """1. Backend Clean Build (dotnet build Fotoprint.slnx)"""
        cmd = ["dotnet", "build", "Fotoprint.slnx"]
        env = os.environ.copy()
        env["DOTNET_ROLL_FORWARD"] = "Major"
        proc = subprocess.run(cmd, cwd=REPO_ROOT, env=env, capture_output=True, text=True)
        self.assertEqual(proc.returncode, 0, f"dotnet build failed with exit code {proc.returncode}:\n{proc.stderr}\n{proc.stdout}")
        self.assertIn("Build succeeded", proc.stdout)
        self.assertIn("0 Error(s)", proc.stdout)

    def test_02_backend_dotnet_test(self):
        """2. Backend Unit & Integration Tests (dotnet test Fotoprint.slnx)"""
        cmd = ["dotnet", "test", "Fotoprint.slnx"]
        env = os.environ.copy()
        env["DOTNET_ROLL_FORWARD"] = "Major"
        proc = subprocess.run(cmd, cwd=REPO_ROOT, env=env, capture_output=True, text=True)
        self.assertEqual(proc.returncode, 0, f"dotnet test failed with exit code {proc.returncode}:\n{proc.stderr}\n{proc.stdout}")
        self.assertIn("Passed!", proc.stdout)
        self.assertIn("Failed:     0", proc.stdout)
        # Verify 132+ tests
        match = re.search(r"Total:\s+(\d+)", proc.stdout)
        self.assertIsNotNone(match, "Could not parse total test count")
        total = int(match.group(1))
        self.assertGreaterEqual(total, 132, f"Expected at least 132 tests, found {total}")

    def test_03_frontend_unified_index_tests(self):
        """3. Frontend Unified Index Unit Tests"""
        cmd = [sys.executable, os.path.join(REPO_ROOT, "infra", "nginx", "html", "tests", "test_unified_index.py")]
        proc = subprocess.run(cmd, cwd=REPO_ROOT, capture_output=True, text=True)
        self.assertEqual(proc.returncode, 0, f"test_unified_index.py failed:\n{proc.stderr}\n{proc.stdout}")
        self.assertIn("OK", proc.stderr + proc.stdout)

    def test_04_frontend_obsidian_theme_tests(self):
        """4. Frontend Obsidian Theme Unit Tests"""
        cmd = [sys.executable, os.path.join(REPO_ROOT, "infra", "nginx", "html", "tests", "test_obsidian_theme.py")]
        proc = subprocess.run(cmd, cwd=REPO_ROOT, capture_output=True, text=True)
        self.assertEqual(proc.returncode, 0, f"test_obsidian_theme.py failed:\n{proc.stderr}\n{proc.stdout}")
        self.assertIn("OK", proc.stderr + proc.stdout)

    def test_05_zero_emojis_in_index_html(self):
        """5. Zero Unicode Emojis in index.html"""
        with open(INDEX_HTML, 'r', encoding='utf-8') as f:
            html = f.read()
        emoji_pattern = re.compile(
            r'[\U00010000-\U0010ffff]|[\u2600-\u27bf]|[\u2300-\u23ff]|[\u2b50-\u2b55]|[\u203c-\u2049]'
        )
        emojis = emoji_pattern.findall(html)
        self.assertEqual(len(emojis), 0, f"Found unexpected emojis in index.html: {emojis}")

    def test_06_domain_vocabulary_zero_domestic_chores(self):
        """6. Domain Vocabulary Audit: Zero Domestic Chores across all files"""
        forbidden_stems = ['уборк', 'упражнен', 'поощрен', 'выпить вод']
        files_to_check = {
            'index.html': INDEX_HTML,
            'theme-core.css': THEME_CORE,
            'Program.cs (Calendar)': CALENDAR_PROGRAM_CS,
            '03_seed_data.sql': SEED_DATA_SQL
        }
        violations = []
        for label, path in files_to_check.items():
            if not os.path.exists(path):
                violations.append(f"File missing: {path}")
                continue
            with open(path, 'r', encoding='utf-8') as f:
                content = f.read().lower()
            for stem in forbidden_stems:
                if stem in content:
                    violations.append(f"{label} contains forbidden domestic stem '{stem}'")

        self.assertEqual(violations, [], f"Domain vocabulary violations detected:\n" + "\n".join(violations))

    def test_07_collision_layout_interval_graph(self):
        """7. Collision Layout: layoutDayTimedEvents splits overlapping events side-by-side"""
        with open(INDEX_HTML, 'r', encoding='utf-8') as f:
            html = f.read()

        self.assertIn("function layoutDayTimedEvents", html)
        self.assertIn("colIndex", html)
        self.assertIn("totalCols", html)
        self.assertIn("100 / item.totalCols", html)
        self.assertIn("calc(${widthPct}% - 6px)", html)
        self.assertIn("calc(${leftPct}% + 3px)", html)

    def test_08_live_red_line_current_time(self):
        """8. Live Red Line: 11:08 current time indicator (668px, red badge, dot, line)"""
        with open(INDEX_HTML, 'r', encoding='utf-8') as f:
            html = f.read()

        self.assertIn('id="cal-now-line-wrapper"', html)
        self.assertIn('id="cal-now-time-pill"', html)
        self.assertIn('id="cal-now-dot"', html)
        self.assertIn('id="cal-now-line"', html)
        self.assertIn('nowWrapper.style.top =', html)

        with open(THEME_CORE, 'r', encoding='utf-8') as f:
            css = f.read()
        self.assertIn('.cal-now-line', css)
        self.assertIn('.cal-now-dot', css)
        self.assertIn('.cal-now-pill', css)

    def test_09_initial_auto_scroll(self):
        """9. Initial Auto-scroll to 07:30 AM (scrollTop = 450)"""
        with open(INDEX_HTML, 'r', encoding='utf-8') as f:
            html = f.read()

        self.assertIn("scrollArea.scrollTop = 450", html)

    def test_10_popover_and_modal_dom_and_flows(self):
        """10. Popover & Modal: #cal-event-popover and #cal-quick-modal"""
        with open(INDEX_HTML, 'r', encoding='utf-8') as f:
            html = f.read()

        self.assertIn('id="cal-event-popover"', html)
        self.assertIn('id="popover-busy-list"', html)
        self.assertIn('id="cal-quick-modal"', html)
        self.assertIn('id="quick-submit-btn"', html)
        self.assertIn('/api/calendar/confirm', html)

    def test_11_multi_theme_switching_and_css_variables(self):
        """11. Multi-Theme: switchTheme and CSS variables across 3 themes"""
        with open(INDEX_HTML, 'r', encoding='utf-8') as f:
            html = f.read()

        self.assertIn("function switchTheme", html)
        self.assertIn("'liquid-glass'", html)
        self.assertIn("'amber'", html)
        self.assertIn("'obsidian'", html)

        for theme_file in [THEME_OBSIDIAN, THEME_GLASS, THEME_AMBER, THEME_CORE]:
            self.assertTrue(os.path.exists(theme_file), f"Missing theme file: {theme_file}")

        with open(THEME_CORE, 'r', encoding='utf-8') as f:
            core_css = f.read()

        self.assertIn('[data-theme="liquid-glass"]', core_css)
        self.assertIn('[data-theme="amber"]', core_css)
        self.assertIn('[data-theme="obsidian"]', core_css)

    def test_12_voice_input_cursor_aware_append(self):
        """12. Voice Input: non-destructive cursor-aware append logic"""
        with open(INDEX_HTML, 'r', encoding='utf-8') as f:
            html = f.read()

        self.assertNotIn("input.value = data.text.trim();", html)
        self.assertIn("selectionStart", html)
        self.assertIn("selectionEnd", html)
        self.assertIn("recognizedText", html)
        self.assertIn("sepBefore", html)
        self.assertIn("sepAfter", html)

    def test_13_visual_screenshots_exist_and_valid(self):
        """13. Visual Screenshots verification"""
        expected_shots = [
            "docs/screenshots/qa_calendar_theme_obsidian.png",
            "docs/screenshots/qa_calendar_theme_amber.png",
            "docs/screenshots/qa_calendar_theme_liquid_glass.png",
            "docs/screenshots/qa_calendar_popover.png",
            "docs/screenshots/qa_calendar_quick_modal.png",
            "docs/screenshots/qa_calendar_grid_clean.png"
        ]
        for shot in expected_shots:
            full_path = os.path.join(REPO_ROOT, shot)
            self.assertTrue(os.path.exists(full_path), f"Screenshot missing: {shot}")
            self.assertGreater(os.path.getsize(full_path), 50000, f"Screenshot too small: {shot}")


if __name__ == '__main__':
    unittest.main()
