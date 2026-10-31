//! BepInEx config model shared by the desktop app.
//!
//! Kept dependency-free so it can be tested without the Tauri toolchain.

use std::fs;
use std::io;
use std::path::Path;

#[derive(Debug, Clone, PartialEq)]
pub struct ModConfig {
    pub player_count: i32,
    pub auto_hot_join: bool,
    pub dynamic_split_screen: bool,
    pub dialogue_no_freeze: bool,
    pub bulk_tithes: bool,
    pub merge_distance: f64,
    pub split_distance: f64,
    pub transition_ms: i32,
    pub state_dwell_ms: i32,
    pub padding: f64,
    pub max_split_size: f64,
    pub dialogue_exit_radius: f64,
    pub tithe_radius: f64,
    pub tithe_key: String,
    pub tithe_cooldown: f64,
}

impl Default for ModConfig {
    fn default() -> Self {
        Self {
            player_count: 4,
            auto_hot_join: true,
            dynamic_split_screen: true,
            dialogue_no_freeze: true,
            bulk_tithes: true,
            merge_distance: 12.0,
            split_distance: 18.0,
            transition_ms: 250,
            state_dwell_ms: 500,
            padding: 1.5,
            max_split_size: 16.0,
            dialogue_exit_radius: 6.0,
            tithe_radius: 8.0,
            tithe_key: "T".to_string(),
            tithe_cooldown: 2.0,
        }
    }
}

impl ModConfig {
    /// Parses the subset of a BepInEx config this app owns. Unknown sections and
    /// keys are ignored; missing values keep their defaults.
    pub fn parse(text: &str) -> Self {
        let mut config = Self::default();
        let mut section = String::new();

        for raw_line in text.lines() {
            let line = raw_line.trim().trim_start_matches('\u{feff}');
            if line.is_empty() || line.starts_with('#') || line.starts_with(';') {
                continue;
            }

            if line.starts_with('[') && line.ends_with(']') {
                section = line[1..line.len() - 1].trim().to_string();
                continue;
            }

            let Some((key, value)) = line.split_once('=') else {
                continue;
            };
            let key = key.trim();
            let value = value.trim();

            match (section.as_str(), key) {
                ("General", "PlayerCount") => {
                    if let Ok(parsed) = value.parse() {
                        config.player_count = parsed;
                    }
                }
                ("Features", "AutoHotJoin") => {
                    config.auto_hot_join = parse_bool(value, config.auto_hot_join);
                }
                ("Features", "DynamicSplitScreen") => {
                    config.dynamic_split_screen =
                        parse_bool(value, config.dynamic_split_screen);
                }
                ("Features", "DialogueNoFreeze") => {
                    config.dialogue_no_freeze =
                        parse_bool(value, config.dialogue_no_freeze);
                }
                ("Features", "BulkTithes") => {
                    config.bulk_tithes = parse_bool(value, config.bulk_tithes);
                }
                ("Camera", "MergeDistance") => {
                    if let Ok(parsed) = value.parse() {
                        config.merge_distance = parsed;
                    }
                }
                ("Camera", "SplitDistance") => {
                    if let Ok(parsed) = value.parse() {
                        config.split_distance = parsed;
                    }
                }
                ("Camera", "TransitionMs") => {
                    if let Ok(parsed) = value.parse() {
                        config.transition_ms = parsed;
                    }
                }
                ("Camera", "StateDwellMs") => {
                    if let Ok(parsed) = value.parse() {
                        config.state_dwell_ms = parsed;
                    }
                }
                ("Camera", "Padding") => {
                    if let Ok(parsed) = value.parse() {
                        config.padding = parsed;
                    }
                }
                ("Camera", "MaxSplitSize") => {
                    if let Ok(parsed) = value.parse() {
                        config.max_split_size = parsed;
                    }
                }
                ("Dialogue", "ExitRadius") => {
                    if let Ok(parsed) = value.parse() {
                        config.dialogue_exit_radius = parsed;
                    }
                }
                ("Tithes", "Radius") => {
                    if let Ok(parsed) = value.parse() {
                        config.tithe_radius = parsed;
                    }
                }
                ("Tithes", "Key") => {
                    let normalized = normalize_shortcut(value);
                    if !normalized.is_empty() {
                        config.tithe_key = normalized;
                    }
                }
                ("Tithes", "Cooldown") => {
                    if let Ok(parsed) = value.parse() {
                        config.tithe_cooldown = parsed;
                    }
                }
                _ => {}
            }
        }

        config
    }

    pub fn render(&self) -> String {
        format!(
            "## Remote Play Enhanced\n\
             ## Generated by the desktop app. Restart the game after saving.\n\
             \n\
             [General]\n\
             PlayerCount = {}\n\
             \n\
             [Features]\n\
             AutoHotJoin = {}\n\
             DynamicSplitScreen = {}\n\
             DialogueNoFreeze = {}\n\
             BulkTithes = {}\n\
             \n\
             [Camera]\n\
             MergeDistance = {}\n\
             SplitDistance = {}\n\
             TransitionMs = {}\n\
             StateDwellMs = {}\n\
             Padding = {}\n\
             MaxSplitSize = {}\n\
             \n\
             [Dialogue]\n\
             ExitRadius = {}\n\
             \n\
             [Tithes]\n\
             Radius = {}\n\
             Key = {}\n\
             Cooldown = {}\n",
            self.player_count,
            self.auto_hot_join,
            self.dynamic_split_screen,
            self.dialogue_no_freeze,
            self.bulk_tithes,
            self.merge_distance,
            self.split_distance,
            self.transition_ms,
            self.state_dwell_ms,
            self.padding,
            self.max_split_size,
            self.dialogue_exit_radius,
            self.tithe_radius,
            self.tithe_key,
            self.tithe_cooldown,
        )
    }

    pub fn validate(&self) -> Result<(), Vec<String>> {
        let mut errors = Vec::new();

        if !(2..=4).contains(&self.player_count) {
            errors.push("PlayerCount must be between 2 and 4.".to_string());
        }
        if self.split_distance <= self.merge_distance {
            errors.push("SplitDistance must be greater than MergeDistance.".to_string());
        }
        if !(50..=1500).contains(&self.transition_ms) {
            errors.push("TransitionMs must be between 50 and 1500.".to_string());
        }
        if !(0..=5000).contains(&self.state_dwell_ms) {
            errors.push("StateDwellMs must be between 0 and 5000.".to_string());
        }
        if !(0.0..=20.0).contains(&self.padding) {
            errors.push("Padding must be between 0 and 20.".to_string());
        }
        if !(1.0..=100.0).contains(&self.max_split_size) {
            errors.push("MaxSplitSize must be between 1 and 100.".to_string());
        }
        if !(1.0..=50.0).contains(&self.dialogue_exit_radius) {
            errors.push("Dialogue.ExitRadius must be between 1 and 50.".to_string());
        }
        if !(1.0..=50.0).contains(&self.tithe_radius) {
            errors.push("Tithes.Radius must be between 1 and 50.".to_string());
        }
        if !(0.5..=30.0).contains(&self.tithe_cooldown) {
            errors.push("Tithes.Cooldown must be between 0.5 and 30.".to_string());
        }
        if !is_valid_shortcut(&self.tithe_key) {
            errors.push("Tithes.Key must be a shortcut such as T or LeftControl + T.".to_string());
        }

        if errors.is_empty() {
            Ok(())
        } else {
            Err(errors)
        }
    }

    pub fn write(&self, path: &Path) -> io::Result<()> {
        if let Err(errors) = self.validate() {
            return Err(io::Error::new(io::ErrorKind::InvalidInput, errors.join(" ")));
        }
        if let Some(parent) = path.parent() {
            fs::create_dir_all(parent)?;
        }
        fs::write(path, self.render())
    }
}

fn parse_bool(value: &str, fallback: bool) -> bool {
    match value.trim().to_ascii_lowercase().as_str() {
        "true" | "1" | "yes" | "on" => true,
        "false" | "0" | "no" | "off" => false,
        _ => fallback,
    }
}

pub fn normalize_shortcut(value: &str) -> String {
    let mut text = value.trim();
    if text.len() > 17
        && text.ends_with(')')
        && text.to_ascii_lowercase().starts_with("keyboardshortcut(")
    {
        text = &text[17..text.len() - 1];
    }

    text.split('+')
        .map(str::trim)
        .filter(|part| !part.is_empty())
        .collect::<Vec<_>>()
        .join(" + ")
}

pub fn is_valid_shortcut(value: &str) -> bool {
    let normalized = normalize_shortcut(value);
    if normalized.is_empty() {
        return false;
    }

    let parts: Vec<&str> = normalized.split(" + ").collect();
    parts.len() <= 4
        && parts
            .iter()
            .all(|part| part.chars().all(|c| c.is_ascii_alphanumeric() || c == '_'))
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn default_render_matches_shared_fixture() {
        let fixture = include_str!("../../../tests/fixtures/default.cfg");
        assert_eq!(ModConfig::default().render(), fixture);
    }

    #[test]
    fn default_renders_every_section() {
        let text = ModConfig::default().render();
        for needle in [
            "[General]",
            "PlayerCount = 4",
            "[Features]",
            "BulkTithes = true",
            "[Camera]",
            "SplitDistance = 18",
            "[Dialogue]",
            "ExitRadius = 6",
            "[Tithes]",
            "Key = T",
        ] {
            assert!(text.contains(needle), "missing {needle} in {text}");
        }
    }

    #[test]
    fn parse_round_trips_render() {
        let mut original = ModConfig::default();
        original.player_count = 3;
        original.auto_hot_join = false;
        original.merge_distance = 10.5;
        original.split_distance = 20.0;
        original.transition_ms = 400;
        original.tithe_key = "LeftControl + T".to_string();
        original.tithe_cooldown = 1.5;

        let parsed = ModConfig::parse(&original.render());
        assert_eq!(parsed, original);
    }

    #[test]
    fn parse_ignores_comments_unknowns_and_bad_values() {
        let text = r#"
            ## comment
            [General]
            PlayerCount = 3
            Mystery = 42

            [Camera]
            MergeDistance = 10.5
            SplitDistance = not-a-number

            [Tithes]
            Key = KeyboardShortcut(F2)
        "#;

        let config = ModConfig::parse(text);
        assert_eq!(config.player_count, 3);
        assert_eq!(config.merge_distance, 10.5);
        assert_eq!(config.split_distance, 18.0);
        assert_eq!(config.tithe_key, "F2");
    }

    #[test]
    fn validation_rejects_bad_ranges_and_inverted_thresholds() {
        let mut config = ModConfig::default();
        config.player_count = 5;
        config.merge_distance = 20.0;
        config.split_distance = 10.0;
        config.tithe_key = "not a key!".to_string();

        let errors = config.validate().unwrap_err();
        assert!(errors.iter().any(|error| error.contains("PlayerCount")));
        assert!(errors.iter().any(|error| error.contains("SplitDistance")));
        assert!(errors.iter().any(|error| error.contains("Tithes.Key")));
    }

    #[test]
    fn shortcut_normalization_matches_the_mod() {
        assert_eq!(normalize_shortcut("  T "), "T");
        assert_eq!(normalize_shortcut("LeftControl+ T"), "LeftControl + T");
        assert_eq!(normalize_shortcut("KeyboardShortcut(F1)"), "F1");
        assert!(is_valid_shortcut("F1"));
        assert!(!is_valid_shortcut("not a key!"));
    }
}
