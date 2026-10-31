import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';

import {
  DEFAULT_CONFIG,
  SCHEMA,
  capabilityCounts,
  configFromText,
  formatModConfig,
  getPath,
  isValidShortcut,
  normalizeShortcut,
  parseCapabilities,
  parseModConfig,
  setPath,
  validateConfig,
} from '../src/lib/config.js';

const SAMPLE = `## hand-written config
[General]
PlayerCount = 3

[Features]
AutoHotJoin = false
DynamicSplitScreen = true
DialogueNoFreeze = true
BulkTithes = true

[Camera]
MergeDistance = 10.5
SplitDistance = 20
TransitionMs = 400
StateDwellMs = 750
Padding = 2
MaxSplitSize = 14

[Dialogue]
ExitRadius = 5

[Tithes]
Radius = 9
Key = LeftControl + T
Cooldown = 1.5

[Unknown]
Mystery = 42
`;

test('schema paths are unique and defaults populate every path', () => {
  const paths = SCHEMA.map((field) => field.path.join('.'));
  assert.equal(new Set(paths).size, paths.length);

  for (const field of SCHEMA) {
    assert.deepEqual(getPath(DEFAULT_CONFIG, field.path), field.default);
  }
});

test('parses a mod config with comments and unknown keys', () => {
  const parsed = parseModConfig(SAMPLE);

  assert.equal(parsed.values['General.PlayerCount'], 3);
  assert.equal(parsed.values['Features.AutoHotJoin'], false);
  assert.equal(parsed.values['Camera.MergeDistance'], 10.5);
  assert.equal(parsed.values['Camera.TransitionMs'], 400);
  assert.equal(parsed.values['Tithes.Key'], 'LeftControl + T');
  assert.equal(parsed.values['Tithes.Cooldown'], 1.5);
  assert.deepEqual(parsed.unknown, ['Unknown.Mystery']);
  assert.deepEqual(parsed.warnings, []);
});

test('configFromText merges parsed values over defaults', () => {
  const { config, found } = configFromText(SAMPLE);

  assert.equal(found, SCHEMA.length);
  assert.equal(config.general.playerCount, 3);
  assert.equal(config.camera.splitDistance, 20);
  assert.equal(config.camera.padding, 2);
  assert.equal(config.tithes.key, 'LeftControl + T');
});

test('formatting and parsing round-trips the whole model', () => {
  const text = formatModConfig({
    ...DEFAULT_CONFIG,
    general: { playerCount: 2 },
    camera: { ...DEFAULT_CONFIG.camera, splitDistance: 25 },
    tithes: { ...DEFAULT_CONFIG.tithes, key: 'F2' },
  });

  assert.match(text, /^## Remote Play Enhanced/m);
  assert.match(text, /\[General\]\nPlayerCount = 2/);
  assert.match(text, /SplitDistance = 25/);
  assert.match(text, /Key = F2/);

  const { config } = configFromText(text);
  assert.equal(config.general.playerCount, 2);
  assert.equal(config.camera.splitDistance, 25);
  assert.equal(config.tithes.key, 'F2');
});

test('default config matches the shared JS/Rust fixture', () => {
  const fixture = readFileSync(
    new URL('./fixtures/default.cfg', import.meta.url),
    'utf8',
  );
  assert.equal(formatModConfig(DEFAULT_CONFIG), fixture);
  assert.deepEqual(configFromText(fixture).config, DEFAULT_CONFIG);
});

test('booleans use the expected lowercase form', () => {
  const text = formatModConfig({
    ...DEFAULT_CONFIG,
    features: {
      ...DEFAULT_CONFIG.features,
      autoHotJoin: false,
      bulkTithes: true,
    },
  });

  assert.match(text, /AutoHotJoin = false/);
  assert.match(text, /BulkTithes = true/);
});

test('validation catches range, cross-field and shortcut errors', () => {
  const bad = {
    ...DEFAULT_CONFIG,
    general: { playerCount: 5 },
    camera: { ...DEFAULT_CONFIG.camera, mergeDistance: 20, splitDistance: 10 },
    tithes: { ...DEFAULT_CONFIG.tithes, key: 'not a key!' },
  };

  const result = validateConfig(bad);
  assert.equal(result.ok, false);
  assert.ok(result.errors.some((error) => error.path === 'general.playerCount'));
  assert.ok(result.errors.some((error) => error.path === 'camera.splitDistance'));
  assert.ok(result.errors.some((error) => error.path === 'tithes.key'));

  assert.equal(validateConfig(DEFAULT_CONFIG).ok, true);
});

test('shortcut normalization accepts cfg syntax', () => {
  assert.equal(normalizeShortcut('  T  '), 'T');
  assert.equal(normalizeShortcut('LeftControl+ T'), 'LeftControl + T');
  assert.equal(normalizeShortcut('KeyboardShortcut(F1)'), 'F1');
  assert.equal(normalizeShortcut(''), '');

  assert.equal(isValidShortcut('T'), true);
  assert.equal(isValidShortcut('LeftControl + T'), true);
  assert.equal(isValidShortcut('F1'), true);
  assert.equal(isValidShortcut('not a key!'), false);
  assert.equal(isValidShortcut(''), false);
});

test('setPath is immutable', () => {
  const before = { camera: { mergeDistance: 12 } };
  const after = setPath(before, ['camera', 'mergeDistance'], 15);

  assert.equal(before.camera.mergeDistance, 12);
  assert.equal(after.camera.mergeDistance, 15);
  assert.notEqual(before.camera, after.camera);
});

test('capability reports are validated and normalized', () => {
  const report = parseCapabilities(JSON.stringify({
    modVersion: '0.4.0',
    playerCount: 4,
    cotlmpPresent: false,
    unityVersion: '2022.3.62f2',
    assemblyVersion: '0.0.0.0',
    knownBuild: false,
    features: [
      { id: 'four-players', state: 'active', reason: 'four slots' },
      { id: 'per-player-hud', state: 'unavailable', reason: 'two heart groups' },
      { id: 'mystery', state: 'exploded', reason: 'unknown' },
    ],
  }));

  assert.equal(report.ok, true);
  assert.equal(report.modVersion, '0.4.0');
  assert.equal(report.cotlmpPresent, false);
  assert.equal(report.unityVersion, '2022.3.62f2');
  assert.equal(report.knownBuild, false);
  assert.equal(report.features[2].state, 'unavailable');
  assert.deepEqual(capabilityCounts(report), {
    active: 1,
    degraded: 0,
    unavailable: 2,
  });

  const broken = parseCapabilities('{ nope');
  assert.equal(broken.ok, false);
  assert.match(broken.error, /Invalid capability report/);

  const missing = parseCapabilities('');
  assert.equal(missing.ok, false);
  assert.equal(missing.error, null);
});
