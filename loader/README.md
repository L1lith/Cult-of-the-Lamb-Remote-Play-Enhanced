# Standalone loader

This folder contains the public-domain UnityDoorstop 3 proxy used to start the
mod without BepInEx.

- `winhttp.dll` — Doorstop 3 x64 proxy. It loads the assembly named by
  `doorstop_config.ini` before Unity starts.
- `doorstop_config.ini` — points Doorstop at `RemotePlayEnhanced\Bootstrap.dll`.
- `LICENSE-Doorstop.txt` — CC0 1.0 Universal. Doorstop 3 is public domain.

The installed game layout is:

```
Cult of the Lamb/
  winhttp.dll
  doorstop_config.ini
  RemotePlayEnhanced/
    Bootstrap.dll
    RemotePlayEnhanced.dll
    0Harmony.dll
    config.cfg
    capabilities.json
    RemotePlayEnhanced.log
```

`Bootstrap.dll` implements `Doorstop.Loader.Main` (Doorstop 3) and
`Doorstop.Entrypoint.Start` (Doorstop 4). It creates the mod component once
Unity has finished loading the first scene.

Doorstop only runs on Windows here. Linux and macOS would need the matching
Doorstop proxy and launcher script.

Source: https://github.com/NeighTools/UnityDoorstop (legacy branch, Doorstop 3).
