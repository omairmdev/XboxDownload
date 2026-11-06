# Security notes for XboxDownload

This document records a **static security review** of this fork (source code, build scripts, data files and dependencies) and the changes
made as a result. It is written for someone who wants to run the app on their own PC.

> **Scope and limits.** This was a read-through of the source plus building the project and scanning its NuGet packages. It was **not** a
> dynamic test on real Windows/macOS/Xbox hardware, and it cannot vouch for third-party servers the app talks to (see
> [Network connections](#network-connections)) or for binaries you download from somewhere else. Treat it as "no malicious code found,
> here are the real risks", not as a guarantee.

## Verdict

* **No malicious behaviour found.** No obfuscated code, no dynamic code loading (`Assembly.Load`, `eval`, downloaded scripts), no
  embedded executables, no hard-coded credentials, no disabled TLS certificate validation, no keylogging/screen-capture/credential access.
* **No known-vulnerable NuGet packages** (`dotnet list package --vulnerable --include-transitive` is clean, and CI now fails if that changes).
* **But the tool is inherently powerful.** It must run as Administrator/root, runs a DNS server and web server on your network, edits your
  `hosts` file and DNS settings, and (optionally) installs a certificate into your trust store. Those are the app's job, but they are also
  what an attacker would want, so the real risk is *which build you run* and *what you leave running*.

**Strong recommendation:** build it yourself from this repository (the included GitHub Actions workflow does that, with pinned actions),
rather than running a binary from a third-party mirror.

## Findings and status

| # | Finding | Severity | Status |
|---|---------|----------|--------|
| 1 | Always requests **Administrator** (Windows manifest `requireAdministrator`; ports 53/80/443, hosts file, DNS settings need it) | Medium (by design) | Documented. Linux/macOS: the `.app` / plain launcher runs as a normal user; only `run_xboxdownload.*` uses root |
| 2 | **Self-updater and data downloads went through third-party proxies** (`gh-proxy.com`, `ghproxy.net`, `pxy1/pxy2.skydevil.xyz`) with no signature/hash check, then executed the result with admin rights | **High** | **Fixed (default off):** these mirrors are now used only if you set `AllowThirdPartyProxies` to `true` in `Settings.json`. Default is direct HTTPS to GitHub / jsDelivr. See note A |
| 3 | The "third-party rules" feature (`Cealing-Host.json`) was also fetched via those mirrors; those rules decide which domains get a certificate issued by the local CA and are redirected | **High** (MITM enabler) | **Fixed:** same switch as #2 |
| 4 | Updater unpacked into a **predictable** temp folder (`%TEMP%\XboxDownload`) and ran a script from it while elevated (local pre-creation / race on multi-user machines) | Low–Medium | **Fixed:** random per-run folder name |
| 5 | `Process.Start(url, UseShellExecute)` would open any string given to it (files, other URL schemes) | Low | **Fixed:** only absolute `http`/`https` URLs are opened |
| 6 | Captured Xbox game download URLs were silently sent to the author's server (`PUT Game/AddGameUrl`) | Low (privacy) | **Fixed:** now opt-in via `ShareGameUrls` (default `false`) |
| 7 | Destructive disk operations (`diskpart clean/format`, MBR boot-signature write) | Medium (data loss) | Guarded: only removable USB disks (USB tool) or non-system disks (storage tool) are listed, a confirmation is required. **Hardened:** the confirmation dialogs now name the exact disk (index, model, drive letters) |
| 8 | **Linux/macOS only, when run as root:** a self-made root CA (RSA-4096, 10 years, **no name constraints**) is installed in the *system* trust store, and its private key is stored unencrypted in `~/.local/share/XboxDownload/XboxDownload.pfx` (mode 600) | **High if you run as root** | Not changed (needs a design decision). Mitigation: run as a normal user (no CA is created), or use *Menu → System Certificate → Uninstall Root Certificate* to delete it when done. Anyone who can read that `.pfx` can impersonate any HTTPS site for this machine while the CA is installed |
| 9 | **Windows:** each time you press *Start*, a short-lived self-signed certificate (SANs: `*.akamai.net`, `*.akamaihd.net`, `*.akamaized.net` + the local-proxy domains you configured) is added to *Local Machine → Trusted Root* and removed on *Stop*. It is a leaf certificate (not a CA) and its key lives only in memory | Low–Medium | Acceptable by design. If the app crashes it can stay installed – see [cleanup](#cleanup-after-a-crash) |
| 10 | **Windows firewall rule** `XboxDownload` allows *all* inbound traffic to the app on Domain/Private/Public profiles, and is never removed | Low–Medium | Documented (a Wi-Fi network classified "Public" would otherwise block the Xbox). Remove it when you stop using the app |
| 11 | The DNS (UDP 53) and HTTP (TCP 80) servers answer **anyone who can reach them**. Default bind is the selected local IP; on **macOS the default is "Any IP"**; "Any IP" is also needed for the Wi-Fi-hotspot setup | Medium | Documented. Pick *Local IP* unless you need the hotspot mode, and never port-forward 53/80/443 to the PC (open resolver / file server) |
| 12 | *Local upload* serves the chosen folder over HTTP to anyone who can reach the server | Low | Path-traversal checks reviewed and correct (request path is normalised and must stay inside the folder). Only enable it with a dedicated folder |
| 13 | Startup task (`Task.xml`) runs the app at logon with highest privileges | Low | Opt-in (Startup settings dialog) |
| 14 | Update packages are not signed (no hash/signature published) | Medium | Cannot be fixed client-side without publisher support. With the default (direct GitHub) it is equivalent to downloading the release manually. See note A |

**Note A – the in-app updater.** It still points at the *upstream* project's releases (`skydevil88/XboxDownload`). If you build from source and
later accept an update prompt, it would replace your build with the upstream author's prebuilt, unsigned binary (and run it as admin).
Decline the update prompt if you want to stay on your own build. If you tell the maintainer of this fork to repoint `UpdateService.Project`
to the fork's releases, the workflow's tag-release job produces compatible `XboxDownload-<os>-<arch>.zip` assets.

## Network connections

The app talks to these services. Anything listed as *your public IP is visible* simply means the usual: the remote server sees your IP.

| Destination | Why | Notes |
|---|---|---|
| `github.com`, `raw.githubusercontent.com`, `testingcf.jsdelivr.net` | Release check, CDN IP lists (`IP/*.txt`), third-party host rules | Only these by default (see #2) |
| `gh-proxy.com`, `ghproxy.net`, `pxy1/pxy2.skydevil.xyz` | Mirrors of the above | **Only if `AllowThirdPartyProxies` = true** |
| `xbox.skydevil.xyz` | Project web store / game-link database | Used by the Store tab; game URL reporting is opt-in (#6) |
| `displaycatalog.mp.microsoft.com`, `xbox.com`, `apps.microsoft.com` | Microsoft Store lookups | |
| `latest.currency-api.pages.dev` | Exchange rates for the price comparison | |
| `ipapi.co`, `ipinfo.io`, `ip-api.com`, `ip9.com.cn`, `ip.zxinc.org` | Geolocation of IPs in the speed test / "am I in mainland China" check | Your public IP is visible |
| DoH servers (Google, Cloudflare, DNS.SB, AliDNS, DNSPod, 360) | Optional encrypted DNS | If you tick *use proxy* for a DoH server, queries go through the author's `dh1.skydevil.xyz` Cloudflare front (hard-coded IP `104.21.45.47`): off by default |
| CDN IPs in `IP/*.txt` | Speed tests (downloads 30–50 MB per IP) | |
| Upload of "preferred Akamai IPs" to the project server | Crowd-sourced list | Only when the UI language is Simplified Chinese **and** you are detected in mainland China (the *Upload* box is ticked by default in that case; untick it to disable) |

Data files in `IP/` are fetched from the upstream repository, whose maintainer's bot updates them. They only choose *which CDN IP* is
used for a Microsoft/Sony/Nintendo/EA… download domain; game payloads stay protected by the platforms' own integrity checks, but a
compromised list could still send your downloads to a slow or hostile server.

## Cleanup after a crash

A normal *Stop* undoes everything the app changed. After a crash or power loss, check:

1. **Xbox / PC DNS** – the Xbox DNS setting must be set back to *Automatic*; on the PC use *Start → Stop* once, or reset the adapter's DNS to automatic.
2. **hosts file** – delete the block between `# --- Begin XboxDownload Section ---` and `# --- End XboxDownload Section ---` in
   `C:\Windows\System32\drivers\etc\hosts` (or `/etc/hosts`). Then `ipconfig /flushdns` (Windows) / equivalent.
3. **Certificate**
   * Windows: `certlm.msc` → *Trusted Root Certification Authorities* → *Certificates* → delete entries named **XboxDownload**.
   * Linux (Debian/Ubuntu): `sudo rm /usr/local/share/ca-certificates/XboxDownload.crt && sudo update-ca-certificates --fresh` (Fedora: `/etc/pki/ca-trust/source/anchors/` + `update-ca-trust extract`; Arch: `/etc/ca-certificates/trust-source/anchors/` + `trust extract-compat`).
   * macOS: `sudo security delete-certificate -c XboxDownload /Library/Keychains/System.keychain`
   * and delete `XboxDownload.pfx` / `XboxDownload.crt` from the app data folder.
4. **Windows firewall rule** – `netsh advfirewall firewall delete rule name="XboxDownload"`
5. **Startup task** (if enabled) – `schtasks /delete /tn XboxDownload /f`
6. App data / settings: `%LOCALAPPDATA%\XboxDownload` (Windows), `~/Library/Application Support/XboxDownload` (macOS), `~/.local/share/XboxDownload` (Linux).

## Safe-use checklist

* Build from source (Actions → *Build* → *Run workflow*) and compare the published `SHA256SUMS-*.txt`.
* Keep **Listening IP = Local IP**; do not expose ports 53/80/443 to the internet.
* On Linux/macOS prefer running **without root** unless you need port listening; avoid root if you do not need the local-proxy/HTTPS feature.
* Close the app (or press *Stop*) when you are not downloading, and set the console's DNS back to automatic.
* Leave `AllowThirdPartyProxies` and `ShareGameUrls` at `false` unless you understand the trade-off.
* Only use the USB/MBR tools on drives you can afford to erase, and read the disk named in the confirmation dialog.

## Reporting a problem

Open a GitHub issue on this repository. Do not include certificates, private keys, `Settings.json` contents or your public IP.
