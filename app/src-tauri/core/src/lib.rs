//! Dependency-free filesystem helpers shared by the Tauri shell and its tests.

pub mod config;
pub mod steam;

use std::fs;
use std::io;
use std::path::{Path, PathBuf};

pub const MOD_FOLDER: &str = "RemotePlayEnhanced";
pub const MOD_DLL: &str = "RemotePlayEnhanced.dll";
pub const BOOTSTRAP_DLL: &str = "Bootstrap.dll";
pub const HARMONY_DLL: &str = "0Harmony.dll";
pub const LOADER_DLL: &str = "winhttp.dll";
pub const DOORSTOP_CONFIG: &str = "doorstop_config.ini";
pub const CONFIG_FILE: &str = "config.cfg";

pub fn mod_dir(game_dir: &Path) -> PathBuf {
    game_dir.join(MOD_FOLDER)
}

pub fn config_path(game_dir: &Path) -> PathBuf {
    mod_dir(game_dir).join(CONFIG_FILE)
}

pub fn capabilities_path(game_dir: &Path) -> PathBuf {
    mod_dir(game_dir).join("capabilities.json")
}

/// True when our Doorstop proxy and config are present.
pub fn loader_installed(game_dir: &Path) -> bool {
    if !game_dir.join(LOADER_DLL).is_file()
        || !game_dir.join(DOORSTOP_CONFIG).is_file()
    {
        return false;
    }

    fs::read_to_string(game_dir.join(DOORSTOP_CONFIG))
        .map(|text| text.contains("RemotePlayEnhanced"))
        .unwrap_or(false)
}

pub fn mod_installed(game_dir: &Path) -> bool {
    mod_dir(game_dir).join(MOD_DLL).is_file()
        && mod_dir(game_dir).join(BOOTSTRAP_DLL).is_file()
}

pub fn read_capabilities(game_dir: &Path) -> Option<String> {
    fs::read_to_string(capabilities_path(game_dir)).ok()
}

/// Copies a prepared standalone bundle into the game folder.
///
/// Bundle layout:
///
/// ```text
/// winhttp.dll
/// doorstop_config.ini
/// RemotePlayEnhanced/
///   Bootstrap.dll
///   RemotePlayEnhanced.dll
///   0Harmony.dll
/// ```
///
/// The existing `config.cfg` is never overwritten.
pub fn deploy_bundle(game_dir: &Path, bundle_dir: &Path) -> io::Result<PathBuf> {
    let source_mod = bundle_dir.join(MOD_FOLDER);
    for required in [
        bundle_dir.join(LOADER_DLL),
        bundle_dir.join(DOORSTOP_CONFIG),
        source_mod.join(BOOTSTRAP_DLL),
        source_mod.join(MOD_DLL),
        source_mod.join(HARMONY_DLL),
    ] {
        if !required.is_file() {
            return Err(io::Error::new(
                io::ErrorKind::NotFound,
                format!("Standalone bundle is missing {}", required.display()),
            ));
        }
    }

    let destination_loader = game_dir.join(LOADER_DLL);
    if destination_loader.exists() && !loader_installed(game_dir) {
        return Err(io::Error::new(
            io::ErrorKind::AlreadyExists,
            "A different winhttp.dll proxy is already installed. Remove it before installing Remote Play Enhanced.",
        ));
    }

    fs::create_dir_all(game_dir)?;
    fs::copy(bundle_dir.join(LOADER_DLL), &destination_loader)?;
    fs::copy(
        bundle_dir.join(DOORSTOP_CONFIG),
        game_dir.join(DOORSTOP_CONFIG),
    )?;

    copy_tree(&source_mod, &mod_dir(game_dir), true)?;

    // Remove a stale bootstrap filename from older local test builds.
    let stale = mod_dir(game_dir).join("RemotePlayEnhanced.Bootstrap.dll");
    if stale.exists() {
        let _ = fs::remove_file(stale);
    }

    Ok(mod_dir(game_dir))
}

fn copy_tree(source: &Path, destination: &Path, keep_existing_cfg: bool) -> io::Result<()> {
    fs::create_dir_all(destination)?;

    for entry in fs::read_dir(source)? {
        let entry = entry?;
        let source_path = entry.path();
        let file_name = entry.file_name();
        let destination_path = destination.join(&file_name);

        if source_path.is_dir() {
            copy_tree(&source_path, &destination_path, keep_existing_cfg)?;
            continue;
        }

        if keep_existing_cfg && file_name == CONFIG_FILE && destination_path.exists() {
            continue;
        }

        fs::copy(&source_path, &destination_path)?;
    }

    Ok(())
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::time::{SystemTime, UNIX_EPOCH};

    #[test]
    fn paths_match_the_standalone_layout() {
        let game = Path::new(r"C:\Games\Cult of the Lamb");
        assert_eq!(
            config_path(game),
            game.join(MOD_FOLDER).join(CONFIG_FILE)
        );
        assert_eq!(
            capabilities_path(game),
            game.join(MOD_FOLDER).join("capabilities.json")
        );
    }

    #[test]
    fn deploy_copies_the_loader_and_mod_bundle() {
        let root = temp_dir("deploy");
        let game = root.join("Cult of the Lamb");
        let bundle = make_bundle(&root);
        fs::create_dir_all(&game).unwrap();
        fs::create_dir_all(mod_dir(&game)).unwrap();
        fs::write(config_path(&game), b"user config").unwrap();

        let deployed = deploy_bundle(&game, &bundle).unwrap();
        assert_eq!(deployed, mod_dir(&game));
        assert!(loader_installed(&game));
        assert!(mod_installed(&game));
        assert!(game.join(LOADER_DLL).is_file());
        assert!(game.join(DOORSTOP_CONFIG).is_file());
        assert_eq!(fs::read(config_path(&game)).unwrap(), b"user config");

        let _ = fs::remove_dir_all(root);
    }

    #[test]
    fn deploy_refuses_to_clobber_another_proxy() {
        let root = temp_dir("proxy");
        let game = root.join("game");
        let bundle = make_bundle(&root);
        fs::create_dir_all(&game).unwrap();
        fs::write(game.join(LOADER_DLL), b"other loader").unwrap();

        let error = deploy_bundle(&game, &bundle).unwrap_err();
        assert_eq!(error.kind(), io::ErrorKind::AlreadyExists);

        let _ = fs::remove_dir_all(root);
    }

    #[test]
    fn deploy_reports_a_missing_bundle() {
        let root = temp_dir("missing");
        let game = root.join("game");
        fs::create_dir_all(&game).unwrap();

        let error = deploy_bundle(&game, &root.join("nope")).unwrap_err();
        assert_eq!(error.kind(), io::ErrorKind::NotFound);

        let _ = fs::remove_dir_all(root);
    }

    fn make_bundle(root: &Path) -> PathBuf {
        let bundle = root.join("bundle");
        let content = bundle.join(MOD_FOLDER);
        fs::create_dir_all(&content).unwrap();
        fs::write(bundle.join(LOADER_DLL), b"doorstop").unwrap();
        fs::write(
            bundle.join(DOORSTOP_CONFIG),
            b"targetAssembly=RemotePlayEnhanced\\Bootstrap.dll",
        )
        .unwrap();
        fs::write(content.join(BOOTSTRAP_DLL), b"bootstrap").unwrap();
        fs::write(content.join(MOD_DLL), b"mod").unwrap();
        fs::write(content.join(HARMONY_DLL), b"harmony").unwrap();
        bundle
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
