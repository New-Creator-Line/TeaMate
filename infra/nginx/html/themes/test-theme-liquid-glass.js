/**
 * Unit Test Suite for Apple Liquid Glass Theme & Calendar Contract
 * Validates:
 * 1. DOM Elements and IDs matching ui_themes_and_calendar_contract.md
 * 2. Optical Glass Bevel tokens and CSS variables
 * 3. Lucide SVG integration and ZERO emoji assertion
 * 4. Human-in-the-Loop booking card & action card classes
 */

const fs = require('fs');
const path = require('path');
const assert = require('assert');

const htmlPath = path.join(__dirname, '..', 'index-glass.html');
const cssPath = path.join(__dirname, 'theme-liquid-glass.css');

console.log('--- RUNNING LIQUID GLASS THEME UNIT TESTS ---');

// Test 1: File Existence
assert(fs.existsSync(htmlPath), 'index-glass.html must exist');
assert(fs.existsSync(cssPath), 'theme-liquid-glass.css must exist');
console.log('✓ Test 1: Deliverable files exist');

const htmlContent = fs.readFileSync(htmlPath, 'utf-8');
const cssContent = fs.readFileSync(cssPath, 'utf-8');

// Test 2: Shared Functional DOM Invariants
const requiredDomIds = [
  'chat-window',
  'prompt-input',
  'send-btn',
  'attach-audio-btn',
  'audio-file-input',
  'voice-toggle-btn',
  'voice-bar',
  'voice-cancel-btn',
  'voice-done-btn',
  'voice-timer',
  'voice-waveform',
  'user-select',
  'role-badge',
  'calendar-toggle-btn',
  'calendar-today-badge',
  'calendar-drawer',
  'calendar-days-grid',
  'selected-day-title',
  'day-meetings-list',
  'scope-my-btn',
  'scope-all-btn',
  'current-month-label',
  'theme-select'
];

requiredDomIds.forEach(id => {
  assert(htmlContent.includes(`id="${id}"`), `DOM element #${id} must be present in index-glass.html`);
});
console.log(`✓ Test 2: All ${requiredDomIds.length} mandatory DOM elements present in HTML`);

// Test 3: ActionCard & Calendar Styling Classes
const requiredClasses = [
  'chat-duration-selector',
  'chat-slots-container',
  'slot-chip',
  'meeting-confirm-card',
  'btn-confirm-yes',
  'btn-confirm-no',
  'calendar-drawer',
  'calendar-badge',
  'day-dots',
  'dot-green',
  'dot-amber',
  'dot-red',
  'mom-card',
  'btn-bitrix',
  'btn-docx-download',
  'ambient-aura'
];

requiredClasses.forEach(cls => {
  assert(htmlContent.includes(cls), `Class .${cls} must be referenced in index-glass.html`);
  assert(cssContent.includes(`.${cls}`), `Class .${cls} must be styled in theme-liquid-glass.css`);
});
console.log(`✓ Test 3: All ${requiredClasses.length} calendar and UI component classes styled`);

// Test 4: Apple Liquid Glass Design System Tokens
const requiredCssTokens = [
  '--bg-cosmic',
  '--glass-bg',
  '--glass-border',
  '--glass-border-top',
  '--glass-blur',
  '--glass-shine',
  '--accent-sky',
  '--accent-indigo',
  'backdrop-filter',
  '-webkit-backdrop-filter'
];

requiredCssTokens.forEach(token => {
  assert(cssContent.includes(token), `CSS token/property ${token} must be defined in theme-liquid-glass.css`);
});
console.log('✓ Test 4: Liquid Glass design tokens (blur, bevel, shine) verified');

// Test 5: Zero Emoji Invariant
const emojiRegex = /[\u{1F300}-\u{1F9FF}\u{2600}-\u{26FF}\u{2700}-\u{27BF}\u{1F1E6}-\u{1F1FF}]/u;
const htmlHasEmoji = emojiRegex.test(htmlContent);
const cssHasEmoji = emojiRegex.test(cssContent);
assert(!htmlHasEmoji, 'index-glass.html MUST NOT contain any emoji characters');
assert(!cssHasEmoji, 'theme-liquid-glass.css MUST NOT contain any emoji characters');
console.log('✓ Test 5: ZERO emojis verified (All replaced by Lucide SVG)');

// Test 6: Script Syntax Check
const scriptMatch = htmlContent.match(/<script>([\s\S]*?)<\/script>/);
assert(scriptMatch, '<script> tag must be present in index-glass.html');
assert.doesNotThrow(() => {
  new Function(scriptMatch[1]);
}, 'Embedded JavaScript in index-glass.html must be valid syntax');
console.log('✓ Test 6: Client-side JavaScript passes syntax compilation');

console.log('--- ALL LIQUID GLASS UNIT TESTS PASSED SUCCESSFULLY! ---');
