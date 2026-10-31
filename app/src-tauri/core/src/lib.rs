//! Dependency-free filesystem helpers shared by the Tauri shell and its tests.

pub mod config;
pub mod steam;

use std::fs;
use std::io;
use std::path::{Path, PathBuf};

pub const CONFIG_FILE: &str = "com.l1lith.cotl.remoteplayenhanced.cfg";
pub const MOD_DLL: &str = "RemotePlayEnhanced.dll";
pub const PLUGIN_FOLDER: &str = "RemotePlayEnhanced";

pub fn config_path(game_dir: &Path) -> PathBuf {
    game_dir
        .join("BepInEx")
        .join("config")
        .join(CONFIG_FILE)
}

pub fn plugin_dir(game_dir: &Path) -> PathBuf {
    game_dir
        .join("BepInEx")
        .join("plugins")
        .join(PLUGIN_FOLDER)
}

pub fn capabilities_path(game_dir: &Path) -> PathBuf {
    plugin_dir(game_dir).join("capabilities.json")
}

pub fn bepinex_installed(game_dir: &Path) -> bool {
    game_dir
        .join("BepInEx")
        .join("core")
        .join("BepInEx.dll")
        .is_file()
}

pub fn mod_installed(game_dir: &Path) -> bool {
    plugin_dir(game_dir).join(MOD_DLL).is_file()
}

pub fn read_capabilities(game_dir: &Path) -> Option<String> {
    fs::read_to_string(capabilities_path(game_dir)).ok()
}

/// Copies the bundled DLL into the game's BepInEx plugin folder.
pub fn deploy_mod(game_dir: &Path, source_dll: &Path) -> io::Result<PathBuf> {
    if !source_dll.is_file() {
        return Err(io::Error::new(
            io::ErrorKind::NotFound,
            format!(
                "Bundled mod not found at {}. Build the mod first or place {} in app/src-tauri/resources/.",
                source_dll.display(),
                MOD_DLL
            ),
        ));
    }

    let destination_dir = plugin_dir(game_dir);
    fs::create_dir_all(&destination_dir)?;

    let destination = destination_dir.join(MOD_DLL);
    fs::copy(source_dll, &destination)?;
    Ok(destination)
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::time::{SystemTime, UNIX_EPOCH};

    #[test]
    fn paths_match_the_mod_layout() {
        let game = Path::new(r"C:\Games\Cult of the Lamb");
        assert_eq!(
            config_path(game),
            game.join("BepInEx").join("config").join(CONFIG_FILE)
        );
        assert_eq!(
            capabilities_path(game),
            game
                .join("BepInEx")
                .join("plugins")
                .join(PLUGIN_FOLDER)
                .join("capabilities.json")
        );
    }

    #[test]
    fn deploy_copies_the_dll_and_creates_folders() {
        let root = temp_dir("deploy");
        let game = root.join("Cult of the Lamb");
        let source = root.join("RemotePlayEnhanced.dll");
        fs::create_dir_all(&game).unwrap();
        fs::write(&source, b"dll bytes").unwrap();

        let deployed = deploy_mod(&game, &source).unwrap();
        assert_eq!(deployed, plugin_dir(&game).join(MOD_DLL));
        assert_eq!(fs::read(&deployed).unwrap(), b"dll bytes");
        assert!(mod_installed(&game));

        let _ = fs::remove_dir_all(root);
    }

    #[test]
    fn deploy_reports_a_missing_bundle() {
        let root = temp_dir("missing");
        let game = root.join("game");
        fs::create_dir_all(&game).unwrap();

        let error = deploy_mod(&game, &root.join("nope.dll")).unwrap_err();
        assert_eq!(error.kind(), io::ErrorKind::NotFound);

        let _ = fs::remove_dir_all(root);
    }

    fn temp_dir(label: &str) -> PathBuf {
        let unique = SystemTime::now()
            .duration_since(UNIX_EPOCH)
            .unwrap()
            .as_nanos();
        let path = std::env::temp_dir().join(format!(
            "rpe-core-{}-{}-{}",
            label,
            std::process::id(),
            unique
        ));
        fs::create_dir_all(&path).unwrap();
        path
    }
}
