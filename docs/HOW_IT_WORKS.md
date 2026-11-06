# How XboxDownload works

XboxDownload is a cross-platform desktop app (C# / .NET 10 / Avalonia UI, MVVM) that turns your PC into a small
**DNS server + HTTP/HTTPS server** for your home network. Devices that use the PC as their DNS server (an Xbox, PlayStation,
Switch, or the PC itself) are steered to faster download servers, and the PC can also serve game files to the console directly.

It does **not** patch any game or console; it only answers DNS questions, answers HTTP requests with redirects/files, and edits the `hosts` file.

---

## 1. The big picture

```mermaid
flowchart LR
    XBOX["Xbox / PlayStation / Switch<br/>DNS server = the PC's IP"]
    subgraph PC["PC running XboxDownload (admin / root)"]
        direction TB
        DNS["DNS server<br/>UDP 53"]
        HTTP["HTTP server<br/>TCP 80"]
        HTTPS["HTTPS server<br/>TCP 443<br/>(optional local proxy)"]
        FILES[("Local upload folder<br/>(optional)")]
        HOSTS[("hosts file")]
        UI["Avalonia UI"]
    end
    UP["Upstream DNS / DoH"]
    CDN["Fast CDN node<br/>chosen by the speed test"]
    GH["github.com<br/>IP lists, releases"]

    XBOX <-->|"1 asks for assets1.xboxlive.com; PC answers with its own IP"| DNS
    XBOX <-->|"2 HTTP GET; PC replies 302 to the .cn host"| HTTP
    XBOX ==>|"3 downloads from the fast node"| CDN
    DNS -.->|"other names are forwarded"| UP
    HTTP -.->|"or serves a file"| FILES
    UI -.->|"adds / removes a section"| HOSTS
    UI -.->|"refreshes IP lists"| GH
```

**Key idea:** the console asks "where is `assets1.xboxlive.com`?" and the PC answers with *its own* address.
When the console then connects, the PC replies with an HTTP `302` redirect to a faster host name/IP (for example the `.cn` CDN domain, which has
servers inside China), or serves the file straight from a folder on the PC (*local upload*).

---

## 2. Life of a download request

```mermaid
sequenceDiagram
    autonumber
    participant X as Xbox
    participant D as DNS service (PC :53)
    participant H as HTTP service (PC :80)
    participant C as Fast CDN node

    X->>D: A? assets1.xboxlive.com
    D-->>X: A = PC IP  (rule: host is "hijacked")
    X->>D: A? some-other-site.com
    D->>D: not in rule map
    D-->>X: forwarded answer (upstream DNS or DoH)
    X->>H: GET /Z/…/game.xvc<br/>Host: assets1.xboxlive.com
    alt Local upload enabled and file exists
        H-->>X: 200/206 file bytes from PC disk (Range supported)
    else Redirect mode
        H-->>X: 302 Location: http://assets1.xboxlive.cn/Z/…/game.xvc
        X->>D: A? assets1.xboxlive.cn
        D-->>X: A = best IP found by Speed Test
        X->>C: GET /Z/…/game.xvc
        C-->>X: game data
    end
```

---

## 3. Components (what each folder does)

```mermaid
flowchart LR
    subgraph V["Views + ViewModels (Avalonia, MVVM)"]
        direction TB
        SV["Service: start / stop listening"]
        ST["Speed Test: find fastest IP"]
        HV["Hosts + CDN: manual rules"]
        SO["Storage + Tools: drive and USB helpers"]
        SR["Store: game links, prices"]
    end
    subgraph S["Services"]
        direction TB
        DCL["DnsConnectionListener<br/>UDP DNS server, DoH client"]
        TCL["TcpConnectionListener<br/>HTTP 302, file upload, TLS"]
        DMG["DnsMappingGenerator<br/>host to IP rule maps"]
        STS["SpeedTestService<br/>ping + ranged download"]
        UPD["UpdateService<br/>releases, IP lists"]
        SET["SettingsManager<br/>Settings.json"]
    end
    subgraph H["Helpers"]
        direction TB
        HH["HostsHelper<br/>write / restore hosts"]
        CH["CertificateHelper<br/>local root CA"]
        FW["FirewallHelper<br/>Windows rule"]
        NA["DnsHelper / NetworkAdapterHelper"]
        MB["MbrHelper + diskpart<br/>PC / Xbox drive mode"]
    end
    SV --> DCL
    SV --> TCL
    ST --> STS
    HV --> DMG
    SO --> MB
    DCL --> DMG
    TCL --> DMG
    SV --> HH
    SV --> FW
    TCL --> CH
    DCL --> NA
    UPD --> ST
    SET -.-> SV
```

| Area | Files | What it does |
|---|---|---|
| Entry point | `Program.cs`, `App.axaml.cs`, `Setup.cs` | Single-instance mutex, Avalonia start-up, dependency injection (`HttpClient` factories) |
| DNS | `Services/DnsConnectionListener.cs`, `Models/Dns/*` | Parses DNS packets, answers rule-matched names with chosen IPs, forwards the rest (plain UDP or DNS-over-HTTPS) |
| HTTP/HTTPS | `Services/TcpConnectionListener.cs` | Plain HTTP redirect/file server; HTTPS with a locally generated certificate (only for the optional "local proxy / SNI" feature) |
| Speed test | `Services/SpeedTestService.cs`, `ViewModels/SpeedTestViewModel.cs` | Pings and downloads a few MB from many candidate IPs (`IP/*.txt`) and ranks them |
| Hosts | `Helpers/IO/HostsHelper.cs` | Adds a clearly delimited section to the OS hosts file while listening; removes it on stop |
| Store | `ViewModels/StoreViewModel.cs` | Looks up a game in the Microsoft Store API and shows its package download links / prices |
| Storage | `Helpers/IO/MbrHelper.cs`, `ViewModels/Storage*` | Flips an external drive's boot-sector signature between PC mode (`55 AA`) and Xbox mode (`99 CC`) so files can be pre-copied |
| Updates | `Services/UpdateService.cs` | Checks GitHub for a newer release, downloads it and swaps the files; also refreshes `IP/*.txt` |
| Data | `IP/*.txt`, `IP/Translation.json`, `Resources/*.txt` | Candidate CDN IP lists (auto-updated by the upstream repo), location-name translations, certificate-domain lists |

---

## 4. Start-up / shut-down sequence (what changes on your PC)

```mermaid
sequenceDiagram
    autonumber
    actor U as You
    participant A as App
    participant OS as Operating system
    U->>A: Click "Start listening"
    A->>OS: Check ports 53/80/443 are free (offer to kill the owner)
    A->>OS: Add firewall rules (Windows)
    A->>OS: Back up current DNS settings, optionally set local DNS to the PC itself
    A->>OS: Write "XboxDownload Section" into the hosts file
    opt Local proxy (SNI) feature enabled
        A->>OS: Windows: add a temporary in-memory certificate to Trusted Root<br/>Linux/macOS: use a persistent local root CA
    end
    A->>OS: Bind UDP 53, TCP 80, TCP 443 (on the chosen local IP or all IPs)
    Note over A,OS: … serving …
    U->>A: Click "Stop listening" / exit
    A->>OS: Close sockets
    A->>OS: Remove its section from the hosts file, restore DNS, flush DNS cache
    A->>OS: Windows: remove temporary root certificate
```

If the app crashes, these changes may stay behind. See [SECURITY.md](../SECURITY.md#cleanup-after-a-crash) for how to undo them.

---

## 5. Speed test & IP lists

```mermaid
flowchart LR
    LIST["IP/IP.XboxCn1.txt, IP.Akamai.txt …<br/>(candidate IPs with location labels)"] --> SEL["You pick a region / search a location"]
    SEL --> PAR["Parallel test:<br/>ICMP/TCP ping + ranged HTTP download<br/>(30–50 MB) with Host header of the real CDN"]
    PAR --> RANK["Sorted by speed"]
    RANK -->|"right-click → Use this IP"| SET["Saved in settings"]
    SET --> MAP["DnsMappingGenerator:<br/>assets1.xboxlive.cn → best IP"]
    MAP --> DNSL["DNS answers / hosts file"]
```

The IP lists are plain text files in `IP/`. A GitHub bot in the upstream repository refreshes them regularly
(the *AutoUpdate IP.\*.txt* commits), and the app downloads the refreshed copies from GitHub on demand.

---

## 6. Release build pipeline (this fork)

```mermaid
flowchart LR
    DEV["push / pull request / manual 'Run workflow'"] --> CHECK["Validate job:<br/>restore, build, vulnerable-package scan"]
    CHECK --> MATRIX{{"Publish matrix"}}
    MATRIX --> W["win-x64 / win-arm64"]
    MATRIX --> L["linux-x64 / linux-arm64"]
    MATRIX --> M["osx-x64 / osx-arm64 (.app bundle)"]
    W & L & M --> ART["Workflow artifacts<br/>XboxDownload-&lt;os&gt;-&lt;arch&gt;.zip + SHA256SUMS"]
    ART -->|"tag v*"| REL["Draft GitHub release"]
```

See `.github/workflows/build.yml`.
