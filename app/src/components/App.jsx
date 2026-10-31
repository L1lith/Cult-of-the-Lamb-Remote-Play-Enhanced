import { createMemo, createSignal, For, onMount, Show } from 'solid-js';

import {
  DEFAULT_CONFIG,
  SCHEMA,
  capabilityCounts,
  configFromText,
  featureLabel,
  formatBepInExConfig,
  getPath,
  parseCapabilities,
  setPath,
  validateConfig,
} from './../lib/config.js';
import {
  checkForUpdates,
  deployMod,
  getAppState,
  launchGame,
  openPath,
  saveConfig,
} from './../lib/bridge.js';

const STATE_LABELS = {
  active: 'Active',
  degraded: 'Degraded',
  unavailable: 'Unavailable',
};

const OVERRIDE_KEY = 'rpe.gameDirOverride';

function Field(props) {
  const field = () => props.field;
  const value = () => getPath(props.config, field().path);

  const onInput = (event) => {
    const raw = event.currentTarget.value;
    const parsed = field().type === 'int'
      ? Number.parseInt(raw, 10)
      : Number.parseFloat(raw);
    props.onChange(Number.isNaN(parsed) ? field().default : parsed);
  };

  return (
    <label class="field">
      <span class="field-head">
        <span>{field().label}</span>
        <span class="field-value">{value()}</span>
      </span>
      <input
        type="number"
        min={field().min}
        max={field().max}
        step={field().step ?? 1}
        value={value()}
        onInput={onInput}
      />
      <span class="field-help">{field().help}</span>
    </label>
  );
}

function Toggle(props) {
  return (
    <button
      type="button"
      class={`toggle ${props.checked ? 'is-on' : ''}`}
      role="switch"
      aria-checked={props.checked}
      onClick={() => props.onChange(!props.checked)}
    >
      <span class="toggle-knob" />
      <span class="toggle-text">{props.checked ? 'On' : 'Off'}</span>
    </button>
  );
}

function StatusBadge(props) {
  return <span class={`badge ${props.state}`}>{STATE_LABELS[props.state] ?? props.state}</span>;
}

export default function App() {
  const [appState, setAppState] = createSignal(null);
  const [config, setConfig] = createSignal(DEFAULT_CONFIG);
  const [savedText, setSavedText] = createSignal(formatBepInExConfig(DEFAULT_CONFIG));
  const [capabilities, setCapabilities] = createSignal(null);
  const [status, setStatus] = createSignal({ kind: 'idle', message: '' });
  const [busy, setBusy] = createSignal(false);
  const [unknownKeys, setUnknownKeys] = createSignal([]);
  const [overrideDir, setOverrideDir] = createSignal('');
  const [updateInfo, setUpdateInfo] = createSignal(null);

  const dirty = createMemo(() => formatBepInExConfig(config()) !== savedText());
  const validation = createMemo(() => validateConfig(config()));
  const counts = createMemo(() => capabilityCounts(capabilities()));

  const setField = (path, value) => {
    setConfig((current) => setPath(current, path, value));
    setStatus({ kind: 'idle', message: '' });
  };

  const applyState = (state) => {
    setAppState(state);
    const parsed = configFromText(state.configText ?? '');
    setConfig(parsed.config);
    setSavedText(formatBepInExConfig(parsed.config));
    setUnknownKeys(parsed.unknown);
    setCapabilities(parseCapabilities(state.capabilitiesText));
    if (state.notice) setStatus({ kind: 'error', message: state.notice });
  };

  const refresh = async (preserveConfig = false) => {
    setBusy(true);
    try {
      const state = await getAppState(overrideDir() || undefined);
      if (preserveConfig) {
        setAppState(state);
        setCapabilities(parseCapabilities(state.capabilitiesText));
      } else {
        applyState(state);
      }
    } catch (error) {
      setStatus({ kind: 'error', message: String(error) });
    } finally {
      setBusy(false);
    }
  };

  onMount(() => {
    const storedOverride = localStorage.getItem(OVERRIDE_KEY);
    if (storedOverride) setOverrideDir(storedOverride);
    refresh();
  });

  const useOverride = () => {
    const value = overrideDir().trim();
    if (value) localStorage.setItem(OVERRIDE_KEY, value);
    else localStorage.removeItem(OVERRIDE_KEY);
    refresh();
  };

  const save = async () => {
    if (!validation().ok) {
      setStatus({ kind: 'error', message: 'Fix the highlighted settings before saving.' });
      return;
    }

    setBusy(true);
    try {
      const text = formatBepInExConfig(config());
      const result = await saveConfig(text, appState()?.gameDir ?? null);
      setSavedText(text);
      setUnknownKeys([]);
      setStatus({ kind: 'ok', message: `Saved to ${result.path}` });
    } catch (error) {
      setStatus({ kind: 'error', message: String(error) });
    } finally {
      setBusy(false);
    }
  };

  const reset = () => {
    setConfig(DEFAULT_CONFIG);
    setStatus({ kind: 'idle', message: 'Defaults restored. Save to apply them.' });
  };

  const deploy = async () => {
    const state = appState();
    if (!state?.gameDir) {
      setStatus({ kind: 'error', message: 'Set the game folder first.' });
      return;
    }

    setBusy(true);
    try {
      const result = await deployMod(state.gameDir);
      setStatus({ kind: 'ok', message: result.message ?? 'Mod deployed.' });
      await refresh(true);
    } catch (error) {
      setStatus({ kind: 'error', message: String(error) });
    } finally {
      setBusy(false);
    }
  };

  const reveal = async (path) => {
    try {
      await openPath(path);
    } catch (error) {
      setStatus({ kind: 'error', message: String(error) });
    }
  };

  const launch = async () => {
    setBusy(true);
    try {
      await launchGame();
      setStatus({ kind: 'ok', message: 'Asked Steam to launch Cult of the Lamb.' });
    } catch (error) {
      setStatus({ kind: 'error', message: String(error) });
    } finally {
      setBusy(false);
    }
  };

  const updates = async () => {
    setBusy(true);
    try {
      const update = await checkForUpdates();
      setUpdateInfo(update);
      setStatus({
        kind: update.available ? 'ok' : 'idle',
        message: update.available
          ? `Version ${update.version} is available.`
          : (update.reason ?? 'You are up to date.'),
      });
    } catch (error) {
      setStatus({ kind: 'error', message: String(error) });
    } finally {
      setBusy(false);
    }
  };

  const installUpdate = async () => {
    const update = updateInfo();
    if (!update?.install) return;

    setBusy(true);
    setStatus({ kind: 'ok', message: 'Downloading update…' });
    try {
      await update.install();
    } catch (error) {
      setStatus({ kind: 'error', message: String(error) });
    } finally {
      setBusy(false);
    }
  };

  const features = SCHEMA.filter((field) => field.type === 'bool');
  const cameraFields = SCHEMA.filter((field) => field.section === 'Camera');
  const dialogueFields = SCHEMA.filter((field) => field.section === 'Dialogue');
  const titheFields = SCHEMA.filter((field) => field.section === 'Tithes');

  return (
    <div class="app-shell">
      <header class="app-header">
        <div>
          <p class="eyebrow">Cult of the Lamb · Steam Remote Play Together</p>
          <h1>Remote Play Enhanced</h1>
          <p class="subtitle">
            Four-player co-op, dynamic split screen, free-moving dialogue and bulk tithes.
          </p>
        </div>
        <div class="header-side">
          <Show when={appState()?.modVersion}>
            <span class="version-pill">mod {appState().modVersion}</span>
          </Show>
          <Show when={appState()?.demo}>
            <span class="version-pill demo">browser demo</span>
          </Show>
        </div>
      </header>

      <Show when={status().message}>
        <div class={`status-line ${status().kind}`}>{status().message}</div>
      </Show>

      <main class="grid">
        <section class="card span-2">
          <div class="card-head">
            <h2>Game install</h2>
            <button class="button ghost" onClick={() => refresh()} disabled={busy()}>Rescan</button>
          </div>

          <Show
            when={appState()?.gameDir}
            fallback={
              <div class="empty-state">
                <p>No Cult of the Lamb install detected in the Steam library folders.</p>
                <div class="inline-row">
                  <input
                    type="text"
                    placeholder="D:\\SteamLibrary\\steamapps\\common\\Cult of the Lamb"
                    value={overrideDir()}
                    onInput={(event) => setOverrideDir(event.currentTarget.value)}
                  />
                  <button class="button" onClick={useOverride} disabled={busy()}>Use folder</button>
                </div>
              </div>
            }
          >
            <div class="facts">
              <div class="fact">
                <span class="fact-label">Folder</span>
                <code>{appState().gameDir}</code>
                <button class="link" onClick={() => reveal(appState().gameDir)}>open</button>
              </div>
              <div class="fact">
                <span class="fact-label">BepInEx</span>
                <StatusBadge state={appState().bepinexInstalled ? 'active' : 'unavailable'} />
              </div>
              <div class="fact">
                <span class="fact-label">Mod DLL</span>
                <StatusBadge state={appState().modInstalled ? 'active' : 'unavailable'} />
                <code>{appState().modVersion ?? 'not installed'}</code>
              </div>
            </div>

            <Show when={!appState().bepinexInstalled}>
              <p class="warning">
                BepInEx 5 is required. Install it into the game folder before deploying
                the mod.
              </p>
            </Show>

            <div class="actions">
              <button
                class="button primary"
                onClick={deploy}
                disabled={busy() || !appState().bepinexInstalled}
              >
                Reinstall / repair mod
              </button>
              <button class="button" onClick={launch} disabled={busy()}>
                Launch through Steam
              </button>
              <button class="button ghost" onClick={updates} disabled={busy()}>
                Check for app updates
              </button>
            </div>
          </Show>

          <Show when={updateInfo()?.available}>
            <div class="update-box">
              <strong>App update {updateInfo().version}</strong>
              <p>{updateInfo().body || 'A new version is available.'}</p>
              <button class="button primary" onClick={installUpdate} disabled={busy()}>
                Install and restart
              </button>
            </div>
          </Show>
        </section>

        <section class="card">
          <div class="card-head">
            <h2>Players</h2>
          </div>
          <div class="player-choices">
            <For each={[2, 3, 4]}>
              {(count) => (
                <button
                  type="button"
                  class={`player-choice ${config().general.playerCount === count ? 'selected' : ''}`}
                  onClick={() => setField(['general', 'playerCount'], count)}
                >
                  <strong>{count}</strong>
                  <span>players</span>
                </button>
              )}
            </For>
          </div>
          <p class="field-help">
            Four-player co-op is the core of the mod. Steam Remote Play Together only
            supports four controllers.
          </p>
        </section>

        <section class="card">
          <div class="card-head">
            <h2>Features</h2>
          </div>
          <div class="feature-list">
            <For each={features}>
              {(field) => (
                <div class="feature-row">
                  <div>
                    <strong>{field.label}</strong>
                    <p>{field.help}</p>
                  </div>
                  <Toggle
                    checked={getPath(config(), field.path)}
                    onChange={(value) => setField(field.path, value)}
                  />
                </div>
              )}
            </For>
          </div>
          <p class="field-help">
            Split screen, dialogue and tithe features switched off are not installed at
            startup; auto hot-join simply stops filling free slots. Restart the game after
            saving.
          </p>
        </section>

        <section class="card">
          <div class="card-head">
            <h2>Capability report</h2>
            <Show when={capabilities()?.ok}>
              <button class="button ghost" onClick={() => refresh(true)} disabled={busy()}>Refresh</button>
            </Show>
          </div>

          <Show
            when={capabilities()?.ok}
            fallback={
              <p class="empty-state">
                {capabilities()?.error ?? 'Launch the game once with the mod installed to generate capabilities.json.'}
              </p>
            }
          >
            <div class="cap-summary">
              <span class="badge active">{counts().active} active</span>
              <span class="badge degraded">{counts().degraded} degraded</span>
              <span class="badge unavailable">{counts().unavailable} unavailable</span>
              <Show when={capabilities().unityVersion}>
                <span class="version-pill">Unity {capabilities().unityVersion}</span>
              </Show>
            </div>

            <Show when={capabilities().knownBuild === false}>
              <p class="warning">
                Unverified game build. Every target is attempted best-effort; check
                the feature rows below.
              </p>
            </Show>

            <Show when={capabilities().cotlmpPresent}>
              <p class="warning">
                COTLMP is installed. Remote Play Enhanced refuses to patch co-op while it
                is present.
              </p>
            </Show>

            <div class="cap-list">
              <For each={capabilities().features}>
                {(feature) => (
                  <div class="cap-row">
                    <div class="cap-name">
                      <strong>{featureLabel(feature.id)}</strong>
                      <StatusBadge state={feature.state} />
                    </div>
                    <p>{feature.reason || 'No reason reported.'}</p>
                  </div>
                )}
              </For>
            </div>
          </Show>
        </section>

        <section class="card">
          <div class="card-head">
            <h2>Camera</h2>
          </div>
          <div class="field-grid">
            <For each={cameraFields}>
              {(field) => (
                <Field
                  field={field}
                  config={config()}
                  onChange={(value) => setField(field.path, value)}
                />
              )}
            </For>
          </div>
          <Show when={validation().errors.some((error) => error.path === 'camera.splitDistance')}>
            <p class="error-text">Split distance must be greater than merge distance.</p>
          </Show>
        </section>

        <section class="card">
          <div class="card-head">
            <h2>Dialogue</h2>
          </div>
          <div class="field-grid">
            <For each={dialogueFields}>
              {(field) => (
                <Field
                  field={field}
                  config={config()}
                  onChange={(value) => setField(field.path, value)}
                />
              )}
            </For>
          </div>
        </section>

        <section class="card">
          <div class="card-head">
            <h2>Tithes</h2>
          </div>
          <div class="field-grid">
            <For each={titheFields}>
              {(field) => (
                <Show
                  when={field.type === 'string'}
                  fallback={
                    <Field
                      field={field}
                      config={config()}
                      onChange={(value) => setField(field.path, value)}
                    />
                  }
                >
                  <label class="field">
                    <span class="field-head">
                      <span>{field.label}</span>
                    </span>
                    <input
                      type="text"
                      value={getPath(config(), field.path)}
                      onInput={(event) => setField(field.path, event.currentTarget.value)}
                    />
                    <span class="field-help">{field.help}</span>
                  </label>
                </Show>
              )}
            </For>
          </div>
        </section>

        <Show when={unknownKeys().length > 0}>
          <section class="card span-2 warning-card">
            <h2>Unknown config keys</h2>
            <p>
              The existing config contains keys this app does not understand. Saving will
              replace the file and drop them.
            </p>
            <code>{unknownKeys().join(', ')}</code>
          </section>
        </Show>
      </main>

      <footer class="save-bar">
        <div>
          <Show
            when={dirty()}
            fallback={<span class="saved-state">Saved</span>}
          >
            <span class="dirty-state">Unsaved changes</span>
          </Show>
          <Show when={!validation().ok}>
            <span class="error-text"> · {validation().errors.length} setting(s) need attention</span>
          </Show>
        </div>
        <div class="save-actions">
          <button class="button ghost" onClick={reset} disabled={busy()}>Reset defaults</button>
          <button
            class="button primary"
            onClick={save}
            disabled={busy() || !validation().ok}
          >
            Save config
          </button>
        </div>
      </footer>
    </div>
  );
}
