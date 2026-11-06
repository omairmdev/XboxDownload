# 🚀 Getting Started

This section explains how to run **XboxDownload** for the first time on
macOS and Linux.

---

## 🍎 macOS First-Time Setup

> ⚠️ **Important**\
> macOS enables Gatekeeper by default. Unsigned applications or scripts
> may be blocked on first launch.

### Step 1 --- Open Terminal
Press `Command + Space`, type **Terminal**, and open it.
### Step 2 --- Prepare and Launch (One-Time Setup)

Run the following:

```bash
cd /path/to/XboxDownload
sudo xattr -dr com.apple.quarantine run_xboxdownload.command XboxDownload.app
chmod +x run_xboxdownload.command
./run_xboxdownload.command
```

Replace `/path/to/XboxDownload` with the actual path to your
XboxDownload directory.

> ✅ This only needs to be done once after downloading.

After the first successful run, you can simply double-click:
-   `run_xboxdownload.command` — Runs with root privileges
-   `XboxDownload.app` — Runs with normal user privileges

> 💡 Most features are available when running with normal privileges.\
> Some advanced features (such as port listening or modifying system
> network settings) may require root privileges.

---

## 🐧 Linux First-Time Setup

### Method 1 — Run via Terminal

```bash
cd /path/to/XboxDownload
chmod +x run_xboxdownload.sh XboxDownload
./run_xboxdownload.sh
```

Replace `/path/to/XboxDownload` with the actual path to your
XboxDownload directory.

---

### Method 2 — Run via File Manager

1.  Open the **XboxDownload** directory.
2.  Locate `run_xboxdownload.sh` or `XboxDownload`.
3.  Right-click → **Properties**.
4.  Enable **Allow executing file as program**.
5.  Close the dialog.
6.  Right-click one of the following files and choose **Run as Program**:
    -   `run_xboxdownload.sh` — Runs with root privileges
    -   `XboxDownload` — Runs with normal user privileges
> 💡 Most features are available when running with normal privileges.\
> Some advanced features (such as port listening or modifying system
> network settings) may require root privileges.

On GNOME, KDE, and some other desktop environments, you can also
double-click the file and select **Run**.

---
