# Building from source

*Part of the [Ballistics](README.md) repository.*
Requires Windows, .NET Framework 4 `csc.exe`, and a local SPT install to reference the
game and BepInEx assemblies.

```powershell
# release: single static DLL (no hot reload) -> recommended for distribution
powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1 -Version 1.0.0 -Install

# debug: shell DLL + Impl/*.bytes (hot reload while developing; not part of the release)
powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1 -Version 1.0.0-dev -Config Debug -Install

# package -> dist/Ballistics-<version>-<config>.zip
powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1 -Version 1.0.0 -Package
```

| Parameter | Default | Meaning |
|---|---|---|
| `-GameDir` | *(required)* | SPT install (reference assemblies + install target), e.g. `C:\SPT` |
| `-OutDir` | `out\` | Intermediate build output |
| `-Version` | `1.0.0` | Semantic version written into the assembly and BepInEx metadata |
| `-Config` | `Release` | `Release` = single DLL, `Debug` = shell + bytes |
| `-Install` | off | Copy into `BepInEx/plugins/Ballistics` |
| `-Package` | off | Produce the zip under `dist/` |

`lang/en.json` is the single source of the English strings: `build.ps1` generates `LangEn.g.cs` from it
before compiling, and copies `lang/*.json` next to the DLL.

The Release build is compiled with `/define:STATIC_BUILD`; in that configuration the runtime assembly
loading code (used by the Debug hot-reload build) is excluded from the output.
