# 📦 XboxDownload – Publishing Notes

XboxDownload can be published as a **self-contained executable** for every supported platform; the target system does not need the .NET runtime.

---

## ✅ Supported platforms

| Platform   | Target RID                  | Notes                                   |
|------------|-----------------------------|-----------------------------------------|
| 🪟 Windows | `win-x64` / `win-arm64`     | Regular and ARM Windows                 |
| 🍎 macOS   | `osx-x64` / `osx-arm64`     | Intel and Apple Silicon                 |
| 🐧 Linux   | `linux-x64` / `linux-arm64` | Mainstream desktop Linux distributions  |

---

## 🪟 Publishing on Windows

1. Open PowerShell or CMD
2. Run the script:
   ```bat
   .\Scripts\publish-win.bat
   ```

---

## 🍎 Publishing on macOS / 🐧 Linux

1. Make the script executable (first time only):
   ```bash
   chmod +x ./Scripts/publish.sh
   ```

2. Run the script and choose a target:
   ```bash
   ./Scripts/publish.sh
   ```

---

## ☁️ Publishing with GitHub Actions

The repository contains `.github/workflows/build.yml`. In GitHub open **Actions → Build → Run workflow**; when it finishes, download
the `XboxDownload-<os>-<arch>` artifacts from the run summary. Pushing a tag such as `v3.0.0.65` also creates a draft GitHub release.

---

## 📁 Output layout

All build output goes to `Scripts/Release/`:

```
Scripts/
├── Release/
│   ├── XboxDownload-linux-arm64.zip
│   ├── XboxDownload-linux-x64.zip
│   ├── XboxDownload-macos-arm64.zip
│   ├── XboxDownload-macos-x64.zip
│   ├── XboxDownload-windows-arm64.zip
│   └── XboxDownload-windows-x64.zip
```

Only the `.zip` archives are kept; temporary output folders are removed automatically.

---

## ⚙️ Build characteristics

- ✅ **Self-contained**: no .NET runtime installation needed
- ✅ **Single-file publish** (Windows/Linux)
- ✅ **Cross-platform builds**
- ✅ **Debug info and symbols removed** for a smaller build
- ✅ **Scripts live in `Scripts/`**:
    - `publish.sh` (macOS/Linux Bash)
    - `publish-win.bat` (Windows batch)
    - `publish-win.ps1` (PowerShell)
