/**
 * Pure configuration model for the Remote Play Enhanced app.
 *
 * The mod reads BepInEx's INI-style config. The app keeps the same shape in
 * memory and renders it back deterministically. Unknown keys are reported
 * before saving, because the mod owns its config file and the app does not
 * silently preserve keys it does not understand.
 *
 * No framework imports here: this module is exercised by node:test.
 */

export const CONFIG_FILE = 'com.l1lith.cotl.remoteplayenhanced.cfg';

export const SCHEMA = [
  {
    section: 'General',
    key: 'PlayerCount',
    path: ['general', 'playerCount'],
    type: 'int',
    default: 4,
    min: 2,
    max: 4,
    label: 'Players',
    help: 'Maximum local players. Steam Remote Play Together supports four.',
  },
  {
    section: 'Features',
    key: 'AutoHotJoin',
    path: ['features', 'autoHotJoin'],
    type: 'bool',
    default: true,
    label: 'Auto hot-join',
    help: 'Fill free slots when extra gamepads connect during an active session.',
  },
  {
    section: 'Features',
    key: 'DynamicSplitScreen',
    path: ['features', 'dynamicSplitScreen'],
    type: 'bool',
    default: true,
    label: 'Dynamic split screen',
    help: 'Split the view when players separate and merge when they regroup.',
  },
  {
    section: 'Features',
    key: 'DialogueNoFreeze',
    path: ['features', 'dialogueNoFreeze'],
    type: 'bool',
    default: true,
    label: 'Dialogue no-freeze',
    help: 'Players keep moving during conversations; walking away ends them.',
  },
  {
    section: 'Features',
    key: 'BulkTithes',
    path: ['features', 'bulkTithes'],
    type: 'bool',
    default: true,
    label: 'Bulk tithes',
    help: 'Collect tithes from every follower in range at once.',
  },
  {
    section: 'Camera',
    key: 'MergeDistance',
    path: ['camera', 'mergeDistance'],
    type: 'float',
    default: 12,
    min: 1,
    max: 200,
    step: 0.5,
    label: 'Merge distance',
    help: 'Player spread below which the view merges.',
  },
  {
    section: 'Camera',
    key: 'SplitDistance',
    path: ['camera', 'splitDistance'],
    type: 'float',
    default: 18,
    min: 1,
    max: 200,
    step: 0.5,
    label: 'Split distance',
    help: 'Player spread above which the view splits. Must exceed merge distance.',
  },
  {
    section: 'Camera',
    key: 'TransitionMs',
    path: ['camera', 'transitionMs'],
    type: 'int',
    default: 250,
    min: 50,
    max: 1500,
    step: 10,
    label: 'Transition (ms)',
    help: 'Viewport blend time.',
  },
  {
    section: 'Camera',
    key: 'StateDwellMs',
    path: ['camera', 'stateDwellMs'],
    type: 'int',
    default: 500,
    min: 0,
    max: 5000,
    step: 50,
    label: 'Layout dwell (ms)',
    help: 'Minimum time between split layouts.',
  },
  {
    section: 'Camera',
    key: 'Padding',
    path: ['camera', 'padding'],
    type: 'float',
    default: 1.5,
    min: 0,
    max: 20,
    step: 0.1,
    label: 'Padding',
    help: 'World units kept around a cluster in its viewport.',
  },
  {
    section: 'Camera',
    key: 'MaxSplitSize',
    path: ['camera', 'maxSplitSize'],
    type: 'float',
    default: 16,
    min: 1,
    max: 100,
    step: 0.5,
    label: 'Max split size',
    help: 'Largest orthographic half-height per viewport.',
  },
  {
    section: 'Dialogue',
    key: 'ExitRadius',
    path: ['dialogue', 'exitRadius'],
    type: 'float',
    default: 6,
    min: 1,
    max: 50,
    step: 0.5,
    label: 'Walk-away radius',
    help: 'Distance the talking player may move before the conversation closes.',
  },
  {
    section: 'Tithes',
    key: 'Radius',
    path: ['tithes', 'radius'],
    type: 'float',
    default: 8,
    min: 1,
    max: 50,
    step: 0.5,
    label: 'Tithe radius',
    help: 'Followers inside this radius pay tithes.',
  },
  {
    section: 'Tithes',
    key: 'Key',
    path: ['tithes', 'key'],
    type: 'string',
    default: 'T',
    label: 'Bulk tithe key',
    help: 'Keyboard shortcut, for example T or LeftControl + T.',
  },
  {
    section: 'Tithes',
    key: 'Cooldown',
    path: ['tithes', 'cooldown'],
    type: 'float',
    default: 2,
    min: 0.5,
    max: 30,
    step: 0.1,
    label: 'Cooldown (s)',
    help: 'Seconds between bulk collections.',
  },
];

export const FEATURE_LABELS = {
  'four-players': 'Four players',
  'per-player-hud': 'Per-player HUD',
  'dynamic-split-screen': 'Dynamic split screen',
  'dialogue-no-freeze': 'Dialogue no-freeze',
  'bulk-tithes': 'Bulk tithes',
};

export const VALID_STATES = ['active', 'degraded', 'unavailable'];

function deepFreeze(value) {
  if (value && typeof value === 'object') {
    for (const child of Object.values(value)) deepFreeze(child);
    Object.freeze(value);
  }
  return value;
}

export function getPath(object, path) {
  return path.reduce((current, key) => (current == null ? undefined : current[key]), object);
}

export function setPath(object, [head, ...tail], value) {
  if (head === undefined) return value;
  const base = object && typeof object === 'object' ? object : {};
  return { ...base, [head]: setPath(base[head], tail, value) };
}

function buildDefaults() {
  return SCHEMA.reduce(
    (config, field) => setPath(config, field.path, field.default),
    {},
  );
}

export const DEFAULT_CONFIG = deepFreeze(buildDefaults());

export function schemaField(section, key) {
  return SCHEMA.find((field) => field.section === section && field.key === key);
}

export function configPathOf(field) {
  return field.path.join('.');
}

function coerceValue(field, raw) {
  const text = String(raw ?? '').trim();
  if (text === '') return field.default;

  if (field.type === 'bool') {
    const lower = text.toLowerCase();
    if (['true', '1', 'yes', 'on'].includes(lower)) return true;
    if (['false', '0', 'no', 'off'].includes(lower)) return false;
    return field.default;
  }

  if (field.type === 'int') {
    const parsed = Number.parseInt(text, 10);
    return Number.isNaN(parsed) ? field.default : parsed;
  }

  if (field.type === 'float') {
    const parsed = Number.parseFloat(text);
    return Number.isNaN(parsed) ? field.default : parsed;
  }

  return text;
}

function formatValue(field, value) {
  if (field.type === 'bool') return value ? 'true' : 'false';
  if (field.type === 'string') return normalizeShortcut(value) || field.default;
  return String(value);
}

/**
 * Parses a BepInEx config. Unknown keys are returned so the UI can warn that
 * saving will drop them.
 */
export function parseBepInExConfig(text) {
  const values = {};
  const unknown = [];
  const warnings = [];
  let section = null;

  for (const rawLine of String(text ?? '').split(/\r?\n/)) {
    const line = rawLine.replace(/^\uFEFF/, '').trim();
    if (line === '' || line.startsWith('#') || line.startsWith(';')) continue;

    const header = line.match(/^\[(.+)\]$/);
    if (header) {
      section = header[1].trim();
      continue;
    }

    const equals = line.indexOf('=');
    if (equals < 0) {
      warnings.push(`Ignoring line without '=': ${line}`);
      continue;
    }

    const key = line.slice(0, equals).trim();
    const raw = line.slice(equals + 1).trim();
    if (!section) {
      unknown.push(key);
      continue;
    }

    const field = schemaField(section, key);
    if (!field) {
      unknown.push(`${section}.${key}`);
      continue;
    }

    values[`${section}.${key}`] = coerceValue(field, raw);
  }

  return { values, unknown, warnings };
}

export function configFromText(text) {
  const parsed = parseBepInExConfig(text);
  let config = DEFAULT_CONFIG;

  for (const field of SCHEMA) {
    const value = parsed.values[`${field.section}.${field.key}`];
    if (value !== undefined) config = setPath(config, field.path, value);
  }

  return {
    config,
    found: Object.keys(parsed.values).length,
    unknown: parsed.unknown,
    warnings: parsed.warnings,
  };
}

export function formatBepInExConfig(config) {
  const lines = [
    '## Remote Play Enhanced',
    '## Generated by the desktop app. Restart the game after saving.',
    '',
  ];

  let currentSection = null;
  for (const field of SCHEMA) {
    if (field.section !== currentSection) {
      currentSection = field.section;
      lines.push(`[${currentSection}]`);
    }
    const value = getPath(config, field.path) ?? field.default;
    lines.push(`${field.key} = ${formatValue(field, value)}`);
    const next = SCHEMA[SCHEMA.indexOf(field) + 1];
    if (!next || next.section !== currentSection) lines.push('');
  }

  return `${lines.join('\n').trimEnd()}\n`;
}

export function normalizeShortcut(input) {
  const stripped = String(input ?? '')
    .trim()
    .replace(/^KeyboardShortcut\((.*)\)$/i, '$1')
    .trim();
  if (!stripped) return '';

  return stripped
    .split('+')
    .map((part) => part.trim())
    .filter(Boolean)
    .join(' + ');
}

export function isValidShortcut(input) {
  const normalized = normalizeShortcut(input);
  if (!normalized) return false;

  const parts = normalized.split(' + ');
  return parts.length <= 4 && parts.every((part) => /^[A-Za-z0-9_]+$/.test(part));
}

export function validateConfig(config) {
  const errors = [];

  for (const field of SCHEMA) {
    const value = getPath(config, field.path);
    const path = configPathOf(field);

    if (field.type === 'bool') {
      if (typeof value !== 'boolean') {
        errors.push({ path, label: field.label, message: 'Must be true or false.' });
      }
      continue;
    }

    if (field.type === 'int') {
      if (!Number.isInteger(value)) {
        errors.push({ path, label: field.label, message: 'Must be a whole number.' });
      } else if (value < field.min || value > field.max) {
        errors.push({
          path,
          label: field.label,
          message: `Must be between ${field.min} and ${field.max}.`,
        });
      }
      continue;
    }

    if (field.type === 'float') {
      if (typeof value !== 'number' || !Number.isFinite(value)) {
        errors.push({ path, label: field.label, message: 'Must be a number.' });
      } else if (value < field.min || value > field.max) {
        errors.push({
          path,
          label: field.label,
          message: `Must be between ${field.min} and ${field.max}.`,
        });
      }
      continue;
    }

    if (field.type === 'string' && !isValidShortcut(value)) {
      errors.push({
        path,
        label: field.label,
        message: 'Use key names such as T, F1, or LeftControl + T.',
      });
    }
  }

  const merge = getPath(config, ['camera', 'mergeDistance']);
  const split = getPath(config, ['camera', 'splitDistance']);
  if (Number.isFinite(merge) && Number.isFinite(split) && split <= merge) {
    errors.push({
      path: 'camera.splitDistance',
      label: 'Split distance',
      message: 'Split distance must be greater than merge distance.',
    });
  }

  return { ok: errors.length === 0, errors };
}

export function parseCapabilities(text) {
  const raw = String(text ?? '').trim();
  if (!raw) return { ok: false, error: null };

  let data;
  try {
    data = JSON.parse(raw);
  } catch (error) {
    return { ok: false, error: `Invalid capability report: ${error.message}` };
  }

  if (!data || typeof data !== 'object' || Array.isArray(data)) {
    return { ok: false, error: 'Capability report is not an object.' };
  }

  const features = (Array.isArray(data.features) ? data.features : [])
    .filter((feature) => feature && typeof feature.id === 'string')
    .map((feature) => ({
      id: feature.id,
      state: VALID_STATES.includes(feature.state) ? feature.state : 'unavailable',
      reason: typeof feature.reason === 'string' ? feature.reason : '',
    }));

  return {
    ok: true,
    modVersion: typeof data.modVersion === 'string' ? data.modVersion : 'unknown',
    playerCount: Number.isInteger(data.playerCount) ? data.playerCount : null,
    cotlmpPresent: data.cotlmpPresent === true,
    features,
  };
}

export function featureLabel(id) {
  return FEATURE_LABELS[id] ?? id;
}

export function capabilityCounts(capabilities) {
  const counts = { active: 0, degraded: 0, unavailable: 0 };
  if (!capabilities?.ok) return counts;

  for (const feature of capabilities.features) {
    if (counts[feature.state] !== undefined) counts[feature.state] += 1;
  }
  return counts;
}
