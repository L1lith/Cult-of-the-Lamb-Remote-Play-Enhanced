//! Steam library discovery without external crates.
//!
//! Only `libraryfolders.vdf` is parsed: the game can be found from its `path`
//! entries without understanding the rest of Valve's key-value format.

use std::collections::BTreeSet;
use std::fs;
use std::path::{Path, PathBuf};

pub const GAME_FOLDER: &str = "Cult of the Lamb";

/// Extracts library paths from `libraryfolders.vdf`.
///
/// Handles both the current `"path" "…"` form and the older numeric-key form,
/// unescapes backslashes, and drops duplicates.
pub fn parse_library_folders(vdf: &str) -> Vec<PathBuf> {
    let mut paths = BTreeSet::new();

    for raw_line in vdf.lines() {
        let line = raw_line.trim();
        if line.is_empty() || line.starts_with("//") {
            continue;
        }

        let tokens = quoted_tokens(line);
        if tokens.len() < 2 {
            continue;
        }

        let candidate = if tokens[0].eq_ignore_ascii_case("path") {
            Some(unescape(&tokens[1]))
        } else if looks_like_path(&tokens[1]) {
            Some(unescape(&tokens[1]))
        } else {
            None
        };

        if let Some(path) = candidate {
            if !path.is_empty() {
                paths.insert(path);
            }
        }
    }

    paths.into_iter().map(PathBuf::from).collect()
}

pub fn library_folders_from_roots(roots: &[PathBuf]) -> Vec<PathBuf> {
    let mut libraries = roots.to_vec();

    for root in roots {
        for relative in [
            "steamapps/libraryfolders.vdf",
            "SteamApps/libraryfolders.vdf",
            "libraryfolders.vdf",
        ] {
            if let Ok(text) = fs::read_to_string(root.join(relative)) {
                libraries.extend(parse_library_folders(&text));
            }
        }
    }

    dedupe(libraries)
}

pub fn candidate_game_dirs(libraries: &[PathBuf]) -> Vec<PathBuf> {
    let mut candidates = Vec::new();
    for library in libraries {
        for steamapps in ["steamapps", "SteamApps"] {
            candidates.push(
                library
                    .join(steamapps)
                    .join("common")
                    .join(GAME_FOLDER),
            );
        }
        candidates.push(library.join("common").join(GAME_FOLDER));
    }

    dedupe(candidates)
}

pub fn find_game_dir(libraries: &[PathBuf]) -> Option<PathBuf> {
    candidate_game_dirs(libraries)
        .into_iter()
        .find(|candidate| looks_like_game_dir(candidate))
}

pub fn looks_like_game_dir(directory: &Path) -> bool {
    const WINDOWS_EXES: [&str; 3] = [
        "Cult Of The Lamb.exe",
        "Cult of the Lamb.exe",
        "CultOfTheLamb.exe",
    ];
    const MAC_BUNDLES: [&str; 2] = ["Cult of the Lamb.app", "Cult Of The Lamb.app"];

    WINDOWS_EXES
        .iter()
        .any(|name| directory.join(name).is_file())
        || MAC_BUNDLES
            .iter()
            .any(|name| directory.join(name).is_dir())
}

pub fn detect_game_dir() -> Option<PathBuf> {
    let roots = steam_roots();
    let libraries = library_folders_from_roots(&roots);
    find_game_dir(&libraries)
}

pub fn steam_roots() -> Vec<PathBuf> {
    let mut roots = Vec::new();

    if let Ok(root) = std::env::var("STEAM_ROOT") {
        roots.push(PathBuf::from(root));
    }

    if let Some(home) = home_dir() {
        #[cfg(target_os = "windows")]
        {
            roots.push(home.join("Steam"));
        }
        #[cfg(target_os = "linux")]
        {
            roots.push(home.join(".steam").join("steam"));
            roots.push(home.join(".steam").join("root"));
            roots.push(home.join(".local").join("share").join("Steam"));
        }
        #[cfg(target_os = "macos")]
        {
            roots.push(home.join("Library").join("Application Support").join("Steam"));
        }
    }

    #[cfg(target_os = "windows")]
    {
        if let Ok(program_files_x86) = std::env::var("ProgramFiles(x86)") {
            roots.push(PathBuf::from(program_files_x86).join("Steam"));
        }
        if let Ok(program_files) = std::env::var("ProgramFiles") {
            roots.push(PathBuf::from(program_files).join("Steam"));
        }
        if let Some(path) = windows_registry_steam_path() {
            roots.push(path);
        }
    }

    dedupe(roots)
        .into_iter()
        .filter(|root| root.is_dir())
        .collect()
}

fn home_dir() -> Option<PathBuf> {
    std::env::var("HOME")
        .or_else(|_| std::env::var("USERPROFILE"))
        .ok()
        .map(PathBuf::from)
}

#[cfg(target_os = "windows")]
fn windows_registry_steam_path() -> Option<PathBuf> {
    let output = std::process::Command::new("reg")
        .args(["query", r"HKCU\Software\Valve\Steam", "/v", "SteamPath"])
        .output()
        .ok()?;
    if !output.status.success() {
        return None;
    }

    let text = String::from_utf8_lossy(&output.stdout);
    for line in text.lines() {
        if let Some((_, value)) = line.split_once("REG_SZ") {
            let path = value.trim();
            if !path.is_empty() {
                return Some(PathBuf::from(path));
            }
        }
    }

    None
}

fn quoted_tokens(line: &str) -> Vec<String> {
    let mut tokens = Vec::new();
    let mut current = String::new();
    let mut inside = false;
    let mut escaped = false;

    for ch in line.chars() {
        if escaped {
            current.push(ch);
            escaped = false;
            continue;
        }

        match ch {
            '\\' if inside => {
                current.push(ch);
                escaped = true;
            }
            '"' => {
                if inside {
                    tokens.push(current.clone());
                    current.clear();
                }
                inside = !inside;
            }
            _ if inside => current.push(ch),
            _ => {}
        }
    }

    tokens
}

fn unescape(value: &str) -> String {
    let mut result = String::with_capacity(value.len());
    let mut chars = value.chars().peekable();

    while let Some(ch) = chars.next() {
        if ch == '\\' {
            match chars.peek() {
                Some('\\') | Some('"') => {
                    result.push(chars.next().unwrap_or(ch));
                }
                _ => result.push(ch),
            }
        } else {
            result.push(ch);
        }
    }

    result
}

fn looks_like_path(value: &str) -> bool {
    value.contains(':')
        || value.starts_with('/')
        || value.starts_with('~')
        || value.contains("\\\\")
}

fn dedupe<T: Ord>(values: Vec<T>) -> Vec<T> {
    values.into_iter().collect::<BTreeSet<_>>().into_iter().collect()
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::time::{SystemTime, UNIX_EPOCH};

    const VDF: &str = r#"
"libraryfolders"
{
    "0"
    {
        "path"        "C:\\Program Files (x86)\\Steam"
        "apps"
        {
            "1313140"        "123456"
        }
    }
    "1"
    {
        "path"        "D:\\SteamLibrary"
    }
    "contentstatsid"        "987654321"
}
"#;

    #[test]
    fn parses_current_library_format() {
        let paths = parse_library_folders(VDF);
        let as_strings: Vec<String> = paths
            .iter()
            .map(|path| path.to_string_lossy().to_string())
            .collect();

        assert_eq!(
            as_strings,
            vec![
                r"C:\Program Files (x86)\Steam".to_string(),
                r"D:\SteamLibrary".to_string(),
            ]
        );
    }

    #[test]
    fn parses_legacy_numeric_entries_without_content_ids() {
        let legacy = r#"
"libraryfolders"
{
    "1"        "E:\\SteamLibrary"
    "contentstatsid"        "12345"
}
"#;

        let paths = parse_library_folders(legacy);
        assert_eq!(paths, vec![PathBuf::from(r"E:\SteamLibrary")]);
    }

    #[test]
    fn candidate_paths_include_case_variants() {
        let candidates = candidate_game_dirs(&[PathBuf::from(r"D:\SteamLibrary")]);
        assert!(candidates.contains(
            &PathBuf::from(r"D:\SteamLibrary").join("steamapps").join("common").join(GAME_FOLDER)
        ));
        assert!(candidates.contains(
            &PathBuf::from(r"D:\SteamLibrary").join("SteamApps").join("common").join(GAME_FOLDER)
        ));
    }

    #[test]
    fn finds_a_game_directory_with_an_exe() {
        let root = temp_dir("find-game");
        let game = root
            .join("steamapps")
            .join("common")
            .join(GAME_FOLDER);
        fs::create_dir_all(&game).unwrap();
        fs::write(game.join("Cult Of The Lamb.exe"), b"stub").unwrap();

        let found = find_game_dir(&[root.clone()]);
        assert_eq!(found, Some(game));

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
