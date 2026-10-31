/**
 * Thin bridge to the Tauri shell. In a plain browser (`npm run dev`) the app
 * still runs with a localStorage-backed config so the UI can be developed and
 * tested without Rust.
 */

const DEMO_CONFIG_KEY = 'rpe.demo.config';

export function isTauri() {
  return typeof window !== 'undefined' && '__TAURI_INTERNALS__' in window;
}

async function invoke(command, args = {}) {
  const api = await import('@tauri-apps/api/core');
  return api.invoke(command, args);
}

export async function getAppState() {
  if (!isTauri()) {
    return {
      demo: true,
      gameDir: null,
      exePath: null,
      loaderInstalled: false,
      modInstalled: false,
      modVersion: null,
      configText: localStorage.getItem(DEMO_CONFIG_KEY) ?? '',
      capabilitiesText: null,
      configPath: null,
      modDir: null,
    };
  }

  return invoke('get_app_state');
}

export async function saveConfig(configText, gameDir = null) {
  if (!isTauri()) {
    localStorage.setItem(DEMO_CONFIG_KEY, configText);
    return { path: 'browser localStorage (demo mode)' };
  }

  return invoke('save_config', { configText, gameDir });
}

export async function deployMod(gameDir) {
  if (!isTauri()) {
    throw new Error('Deploying the mod requires the desktop app.');
  }
  return invoke('deploy_mod', { gameDir });
}

export async function launchGame() {
  if (!isTauri()) {
    throw new Error('Launching the game requires the desktop app.');
  }
  return invoke('launch_game');
}

export async function openPath(path) {
  if (!isTauri() || !path) return;
  return invoke('open_path', { path });
}

export async function checkForUpdates() {
  if (!isTauri()) {
    return { available: false, reason: 'Updates require the desktop app.' };
  }

  const updater = await import('@tauri-apps/plugin-updater');
  const update = await updater.check();
  if (!update) return { available: false };

  return {
    available: true,
    version: update.version,
    body: update.body ?? '',
    install: async () => {
      await update.downloadAndInstall();
      const process = await import('@tauri-apps/plugin-process');
      await process.relaunch();
    },
  };
}
