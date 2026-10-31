use std::fs;
use std::path::{Path, PathBuf};

use rpe_core::config::ModConfig;
use rpe_core::steam;
use serde::Serialize;
use tauri::Manager;
use tauri_plugin_opener::OpenerExt;

/// Cult of the Lamb's Steam application id.
const STEAM_APP_ID: &str = "1313140";

const WINDOWS_EXES: [&str; 3] = [
    "Cult Of The Lamb.exe",
    "Cult of the Lamb.exe",
    "CultOfTheLamb.exe",
];

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
struct DesktopState {
    demo: bool,
    game_dir: Option<String>,
    exe_path: Option<String>,
    loader_installed: bool,
    mod_installed: bool,
    mod_version: Option<String>,
    config_text: String,
    capabilities_text: Option<String>,
    config_path: Option<String>,
    mod_dir: Option<String>,
    notice: Option<String>,
}

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
struct SaveResult {
    path: String,
}

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
struct DeployResult {
    path: String,
    message: String,
}

#[tauri::command(rename_all = "camelCase")]
fn get_app_state(override_dir: Option<String>) -> DesktopState {
    let (game_dir, notice) = resolve_game_dir(override_dir);

    let mut state = DesktopState {
        demo: false,
        game_dir: None,
        exe_path: None,
        loader_installed: false,
        mod_installed: false,
        mod_version: None,
        config_text: String::new(),
        capabilities_text: None,
        config_path: None,
        mod_dir: None,
        notice,
    };

    let Some(game_dir) = game_dir else {
        return state;
    };

    state.game_dir = Some(game_dir.display().to_string());
    state.exe_path = find_executable(&game_dir).map(|path| path.display().to_string());
    state.loader_installed = rpe_core::loader_installed(&game_dir);
    state.mod_installed = rpe_core::mod_installed(&game_dir);

    let config_path = rpe_core::config_path(&game_dir);
    state.config_text = fs::read_to_string(&config_path).unwrap_or_default();
    state.config_path = Some(config_path.display().to_string());

    let mod_dir = rpe_core::mod_dir(&game_dir);
    state.mod_dir = Some(mod_dir.display().to_string());

    state.capabilities_text = rpe_core::read_capabilities(&game_dir);
    state.mod_version = mod_version(state.capabilities_text.as_deref());

    state
}

#[tauri::command(rename_all = "camelCase")]
fn save_config(game_dir: Option<String>, config_text: String) -> Result<SaveResult, String> {
    let (game_dir, notice) = resolve_game_dir(game_dir);
    let Some(game_dir) = game_dir else {
        return Err(notice.unwrap_or_else(|| "Cult of the Lamb folder not found.".to_string()));
    };

    let config = ModConfig::parse(&config_text);
    config.validate().map_err(|errors| errors.join(" "))?;

    let path = rpe_core::config_path(&game_dir);
    config
        .write(&path)
        .map_err(|error| format!("Could not write {}: {error}", path.display()))?;

    Ok(SaveResult {
        path: path.display().to_string(),
    })
}

#[tauri::command(rename_all = "camelCase")]
fn deploy_mod(app: tauri::AppHandle, game_dir: String) -> Result<DeployResult, String> {
    let bundle = bundled_bundle(&app)?;
    let game_dir = PathBuf::from(game_dir);
    if !game_dir.is_dir() {
        return Err(format!("Game folder does not exist: {}", game_dir.display()));
    }

    let deployed = rpe_core::deploy_bundle(&game_dir, &bundle).map_err(|error| {
        format!("Could not install the standalone loader: {error}")
    })?;

    Ok(DeployResult {
        path: deployed.display().to_string(),
        message: format!(
            "Installed the standalone loader and mod into {}",
            game_dir.display()
        ),
    })
}

#[tauri::command(rename_all = "camelCase")]
fn launch_game(app: tauri::AppHandle) -> Result<(), String> {
    app.opener()
        .open_url(format!("steam://rungameid/{STEAM_APP_ID}"), None::<&str>)
        .map_err(|error| format!("Could not ask Steam to launch the game: {error}"))
}

#[tauri::command(rename_all = "camelCase")]
fn open_path(app: tauri::AppHandle, path: String) -> Result<(), String> {
    app.opener()
        .open_path(path, None::<&str>)
        .map_err(|error| format!("Could not open the folder: {error}"))
}

fn resolve_game_dir(override_dir: Option<String>) -> (Option<PathBuf>, Option<String>) {
    if let Some(raw) = override_dir {
        let trimmed = raw.trim();
        if !trimmed.is_empty() {
            let path = PathBuf::from(trimmed);
            if !path.is_dir() {
                return (
                    None,
                    Some(format!("Folder does not exist: {}", path.display())),
                );
            }
            if steam::looks_like_game_dir(&path) {
                return (Some(path), None);
            }

            let nested = path.join(steam::GAME_FOLDER);
            if steam::looks_like_game_dir(&nested) {
                return (Some(nested), None);
            }

            return (
                None,
                Some(format!(
                    "No Cult of the Lamb install found at {}",
                    path.display()
                )),
            );
        }
    }

    (steam::detect_game_dir(), None)
}

fn find_executable(game_dir: &Path) -> Option<PathBuf> {
    WINDOWS_EXES
        .iter()
        .map(|name| game_dir.join(name))
        .find(|path| path.is_file())
        .or_else(|| {
            ["Cult of the Lamb.app", "Cult Of The Lamb.app"]
                .iter()
                .map(|name| game_dir.join(name))
                .find(|path| path.is_dir())
        })
}

fn mod_version(capabilities_text: Option<&str>) -> Option<String> {
    let value: serde_json::Value = serde_json::from_str(capabilities_text?).ok()?;
    value
        .get("modVersion")
        .and_then(|version| version.as_str())
        .map(ToOwned::to_owned)
}

fn bundled_bundle(app: &tauri::AppHandle) -> Result<PathBuf, String> {
    let mut candidates = Vec::new();

    if let Ok(resource_dir) = app.path().resource_dir() {
        candidates.push(resource_dir.join("bundle"));
        candidates.push(resource_dir.join("resources").join("bundle"));
        candidates.push(resource_dir);
    }

    let manifest_resources = PathBuf::from(env!("CARGO_MANIFEST_DIR")).join("resources");
    candidates.push(manifest_resources.join("bundle"));
    candidates.push(manifest_resources);

    candidates
        .into_iter()
        .find(|path| {
            path.join("winhttp.dll").is_file()
                && path.join("RemotePlayEnhanced").join("Bootstrap.dll").is_file()
        })
        .ok_or_else(|| {
            "Standalone loader bundle is missing. Build the mod and bootstrap, then copy them into app/src-tauri/resources/bundle/.".to_string()
        })
}

#[cfg_attr(mobile, tauri::mobile_entry_point)]
pub fn run() {
    tauri::Builder::default()
        .plugin(tauri_plugin_opener::init())
        .plugin(tauri_plugin_updater::Builder::new().build())
        .plugin(tauri_plugin_process::init())
        .invoke_handler(tauri::generate_handler![
            get_app_state,
            save_config,
            deploy_mod,
            launch_game,
            open_path
        ])
        .run(tauri::generate_context!())
        .expect("error while running Remote Play Enhanced");
}
