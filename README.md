# Xbox Download Assistant (XboxDownload)

Target framework: .NET 10.0, Avalonia 12

> **Before you run it:** this tool needs administrator/root rights and changes your DNS/hosts settings.
> Read [SECURITY.md](SECURITY.md) and [docs/HOW_IT_WORKS.md](docs/HOW_IT_WORKS.md) first, and prefer building from source
> (see [Building](#building)) over downloading binaries from third parties.

Get Microsoft Store game download links
---
![Store](images/EN/Store.gif)

Web store: https://xbox.skydevil.xyz

Download Xbox console games with a PC: https://www.youtube.com/watch?v=3F499kh_jfk

Download the app: https://github.com/skydevil88/XboxDownload/releases

Overview
---
Xbox consoles are assigned a download domain according to the user's IP address.
Mainland-China IPs get `assets1.xboxlive.cn`, which has local CDN servers and is fast (except for unpopular games that are not cached).
Overseas IPs get `assets1.xboxlive.com`, which has no China CDN, so traffic leaves the country and is slow.
If a user in China uses an accelerator or proxy, Microsoft treats them as an overseas user and assigns the `.com` domain.

**How the speed-up works:** the app redirects the Xbox download hosts `assets1.xboxlive.com` (and friends) to `assets1.xboxlive.cn` (note 1),
pinning downloads to servers in China. This fixes slow downloads over the international domain, and the same mechanism also accelerates
downloads from the PC Microsoft Store, PlayStation, Nintendo Switch, EA, Battle.net (international), Epic, Ubisoft, Riot Games, Rockstar Games and more.

**Note 1: the `.com` → `.cn` redirect is only enabled when the UI language is Simplified Chinese.**
Outside China you can still use the app to find the fastest CDN IP for your location (Speed Test), pin hosts, and copy download links.

See [docs/HOW_IT_WORKS.md](docs/HOW_IT_WORKS.md) for diagrams of the architecture.

Screenshots
---
**Service** – DNS + HTTP(S) listener, and uploading games from the PC to the Xbox

![Service](images/EN/Service1.png)
![Service](images/EN/Service2.png)

**Speed Test**

![Speed test](images/EN/SpeedTest.png)

**Hosts**

![Hosts](images/EN/Hosts.png)

**CDN**

![CDN](images/EN/Cdn.png)

**Storage**

![Storage](images/EN/Storage.png)

**About**

![About](images/EN/About.png)

Building
---
Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download). The repository also contains a GitHub Actions workflow
(`.github/workflows/build.yml`) that builds Windows/Linux/macOS packages; run it from the **Actions** tab (**Run workflow**) and download the
artifacts. See [XboxDownload/Scripts/publish.md](XboxDownload/Scripts/publish.md) for the local publish scripts.

```bash
dotnet build XboxDownload.sln -c Release
```

1. Basic setup (Nintendo Switch and PlayStation work the same way)
---
If the Xbox is downloading, pause the download first and resume it after the setup is complete.

**Step 1.** Start the app, open **Speed Test → Import IP**, look for IPs near you (the *search location* feature helps), then right-click and choose **Use this IP**.

![Step 2](images/SpeedTest.png)

**Step 2.** Choose a **Local IP** in the same subnet as the Xbox (usually `192.168.x.x`; in the screenshot it is `192.168.50.5` – pick the right one for your network).
Make sure **1. DNS service** and **2. HTTP(S) service** are ticked, then click **Start listening**.

![Step 3](images/Listening.png)

**Step 3.** On the Xbox open **Settings → General → Network settings → Advanced settings → DNS settings → Manual**, set the **primary DNS** to the PC's IP address
(shown at the bottom left of the app as *Local IP*) and leave the secondary DNS empty. (PC Xbox App users can skip this step.)

![Step 4](images/Consoles2.png)

Note: if your Xbox uses IPv6, turn IPv6 off in the router.

![Step 5](images/Consoles3.png)

You can now download. When finished, **set the Xbox DNS back to automatic** – otherwise the Xbox has no internet once the app is closed.

Troubleshooting:

1. *No log output.* Either the wrong Local IP is selected (choose again) or a firewall/"PC manager" security suite is blocking the app – disable it.
2. *Port in use.* Usually answering "Yes" to force-stop the other program works. Otherwise find the process with `netstat -an` and stop it yourself.
3. *Xbox connected through the PC's Wi-Fi hotspot.* Set **Listening IP** to "Any IP", untick **DNS service**, and untick **Set local DNS**.
   The Xbox DNS can then stay on automatic.

![Hotspot](images/Hotspot.png)

2. Uploading downloads from the PC to the Xbox (also works for PC Game Pass, PlayStation, EA)
---
There are two methods. First let the app capture the game's download URL (tick *Show download links*), pause the download, right-click and copy the URL, then download the file on the PC with a download manager.

PC Game Pass games can be captured the same way; install them afterwards with **Tools → Install Microsoft Store games and apps**.

![Game link](images/Gamelink.png)

**Method 1 – local upload.** Point the *local upload folder* to the folder containing the downloaded files, tick *Local upload*, start listening and restart the download on the Xbox: the Xbox pulls the files straight from the PC.

![Local upload](images/LocalUpload.png)

**Method 2 – hard-drive import.** Connect the Xbox external drive to the PC and convert it to *PC mode* in the app (you may need to turn off your antivirus).
Copy the game files onto it and rename them to the *Content ID* (or download straight into the drive's root to skip the copy), then convert it back to *Xbox mode*.
Delete the paused game on the Xbox, plug the drive back in, power on **with internet access (required)**, open Settings → System → Storage, pick the drive and move the game to internal storage.
Do not launch the game from the external drive (it would be downloaded again). Apps cannot be imported this way.

How to rename to the Content ID: in the app go to *Drive → file info → local files → Browse*, select the file and click *Rename local file*.

![Storage 1](images/Storage1.png)
![Storage 2](images/Storage2.png)
![Storage 3](images/Storage3.png)

3. Without keeping a PC on
---
Install Lighttpd or Nginx on OpenWrt and use URL rewriting to redirect to the China CDN:
[README_OpenWrt.md](README_OpenWrt.md)

If another router lets you edit hosts, speed-test the `.com` and `.cn` download domains, find the fastest IP and put them into the hosts file; every download then benefits whichever domain the Xbox uses.

CN domains (Akamai IPs can also be used; they may work better for old or unpopular games that the CDN has not cached):
```
# Group 1: main download domains (shared by PC and consoles)
address=/assets1.xboxlive.cn/<cn IP or Akamai IP>
address=/assets2.xboxlive.cn/<cn IP or Akamai IP>
address=/d1.xboxlive.cn/<cn IP or Akamai IP>
address=/d2.xboxlive.cn/<cn IP or Akamai IP>

# Group 2: download domains for some older Xbox One games (no longer used by new PC/console games)
address=/dlassets.xboxlive.cn/<cn IP or Akamai IP>
address=/dlassets2.xboxlive.cn/<cn IP or Akamai IP>

# Group 3: apps and some PC games use these domains.
# The Xbox app says "This game doesn't support installing to a specific folder. It will be installed with other Windows apps." for these.
address=/dl.delivery.mp.microsoft.com/<cn IP or Akamai IP>
address=/tlu.dl.delivery.mp.microsoft.com/<cn IP or Akamai IP>
```
COM domains (cn IPs cannot be used here):
```
address=/assets1.xboxlive.com/<Akamai IP>
address=/assets2.xboxlive.com/<Akamai IP>
address=/xvcf1.xboxlive.com/<Akamai IP>
address=/xvcf2.xboxlive.com/<Akamai IP>
address=/d1.xboxlive.com/<Akamai IP>
address=/d2.xboxlive.com/<Akamai IP>
address=/dlassets.xboxlive.com/<Akamai IP>
address=/dlassets2.xboxlive.com/<Akamai IP>
```

About
---
![About](images/About.png)

Notes on language
---
The app ships with English (default), Simplified Chinese and Traditional Chinese UI packs (`XboxDownload/Resources/Languages`).
The files in `IP/` are data files: their location labels are intentionally left in Chinese because the app matches against them
(`IP/Translation.json` maps those Chinese names to English).
