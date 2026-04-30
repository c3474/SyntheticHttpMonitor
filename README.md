# Synthetic HTTP Monitor

Windows service that polls HTTP/HTTPS endpoints (status code and optional body regex), logs results, and sends SMTP alerts when targets fail or recover.

## Quick install (what most people want)

**Who builds the zip:** someone with the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) runs:

```powershell
cd tools\SyntheticHttpMonitor
.\Publish-Monitor.ps1 -Package -Zip
```

**Code signing (optional, before the zip is created):** if you have a **code signing** certificate on the build machine, pass its **SHA1 thumbprint** so the publish script signs the three shipped EXEs after publish and **before** `Compress-Archive`. The script uses **`signtool.exe`** (Windows SDK) when it is installed; **otherwise it falls back to `Set-AuthenticodeSignature`**, including **`-IncludeChain All`** when your PowerShell supports it. The cert below is the usual **CurrentUser\My** code-signing cert for this repo’s maintainer machine (replace the thumbprint if yours differs):

```powershell
.\Publish-Monitor.ps1 -Package -Zip -SignCertificateThumbprint 'ff768047fb24eb88cfb6adc93c674a5cea227248'
```

Use **`-SignUseMachineStore`** if the cert lives under **LocalMachine\My** instead of **CurrentUser\My**. Override **`signtool.exe`** with **`-SignToolPath`** if needed. PFX file instead of the store: **`-SignCertificatePath 'D:\path\sign.pfx' -SignCertificatePassword $env:PFX_PASS`**. Default timestamp server is DigiCert; override with **`-SignTimestampServer`**.

If **`signtool`** signs with a cert whose private key has **password / strong key protection**, Windows may prompt **once per EXE** (this repo signs **three** binaries), so **up to three identical password prompts in a row** is normal—not three chances because you mistyped.

Thumbprint (SHA1) from the current user store:

```powershell
Get-ChildItem -Path Cert:\CurrentUser\My -CodeSigningCert | Format-List Subject, Thumbprint
```

That produces **`SyntheticHttpMonitor-win-x64.zip`** next to the script (your release artifact). The zip root is intentionally small: **`readme.md`**, **`SyntheticHttpMonitor.Installer.exe`** (graphical installer; built from **`SyntheticHttpMonitor.Setup.exe`**), and a **`Resources`** folder with the service **`SyntheticHttpMonitor.exe`**, **`SyntheticHttpMonitor.Config.exe`**, example JSON, **`START_HERE.txt`**, and the rest of the payload.

**Who installs on the server:**

1. Copy the zip to the server and **extract** it to a folder (e.g. Desktop\SyntheticHttpMonitor). Read **`readme.md`** at the top level; optional detail lives under **`Resources\START_HERE.txt`**.
2. **Recommended:** double‑click **`SyntheticHttpMonitor.Installer.exe`** and approve the UAC prompt. Choose the install folder (default is under Program Files), then click **Install** or **Upgrade** if the service already exists. Optional: check **Start the service when finished** on a new install.
3. **Configure checks and email:** after install, run **`Resources\SyntheticHttpMonitor.Config.exe`** under the install folder (same folder as the service EXE and the shared self-contained runtime), or use **File → Open installed copy** from anywhere. Use the **Monitored URLs** tab for endpoints and **Notifications** for SMTP and alerting. **Save changes** — if Windows blocks the write, approve the UAC prompt so the file can be saved; when you are editing the **installed** `appsettings.json` under **`Resources`**, the app also tries to **restart the service** for you (and shows a warning if it cannot). The status bar shows a green/red indicator for the Windows service. The editor leaves **`Serilog`** and other sections as they are.
4. If the service did not start automatically, start it after the account is correct:

   ```powershell
   Start-Service -Name SyntheticHttpMonitor
   ```

The installer registers the app under **Settings → Apps → Installed apps** (legacy **Programs and Features** / `appwiz.cpl`). **Uninstall** from there runs **`SyntheticHttpMonitor.Installer.exe /uninstall`** (older entries may still show **`SyntheticHttpMonitor.Setup.exe`**). You can also run **`SyntheticHttpMonitor.Installer.exe /uninstall`** from an elevated command prompt. Uninstall removes the **Windows service** and the list entry; it does **not** delete the install folder (your **`Resources\appsettings.json`** stays for the default layout). To remove files too, delete the install folder after uninstalling (or before, if you no longer need the config).

### Upgrading vs fresh install (Installer.exe)

- **Fresh install:** there is no Windows service with the **internal service name** you entered (default `SyntheticHttpMonitor`). Setup copies files into the folder you choose and runs `sc create` once.
- **In-place upgrade:** the same service name **already exists**. Setup detects the install directory from that service, switches the button to **Upgrade (replace files)**, and shows **package version → installed version** from `SyntheticHttpMonitor.exe`. You do **not** need to uninstall first; binaries are overwritten and **Programs and Features** picks up the new **DisplayVersion** from the replaced service EXE after you run Setup again. **`appsettings.json`**, **`targets.json`**, **`logging.json`**, and **`notifications.json`** are **not overwritten** on upgrade when they already exist (your URLs, Serilog, and alert/SMTP settings stay). New installs still get **`appsettings.json`** from **`appsettings.Example.json`** when needed. **`*.Example.json`** files from the package are still refreshed so you can compare with your live config.
- **Changing only `appsettings.json`:** use **`SyntheticHttpMonitor.Config.exe`** and restart the service — no reinstall required.

**Upgrade to a newer build:** extract the new zip and run **`SyntheticHttpMonitor.Installer.exe`** as Administrator again. Setup detects the existing service, offers **Upgrade (replace files)**, and leaves your existing JSON in place (same rules as in **Upgrading vs fresh install** above).

---

## Requirements

- **Build machine:** .NET 8 SDK and a NuGet source (e.g. `nuget.org`). **Windows PowerShell 5.1** is enough for `Publish-Monitor.ps1` (including `-Zip`; needs the built-in **Microsoft.PowerShell.Archive** module, standard on Windows 10 / Server 2016 and newer).
- **Run machine:** Windows x64. **Self-contained** publish does **not** require a separate .NET runtime on the server.

## Build and publish (details)

```powershell
.\Publish-Monitor.ps1 -OutputPath 'C:\Deploy\SyntheticHttpMonitor'
```

- **`-Package`** — copies `appsettings.Example.json`, `targets.Example.json`, `logging.Example.json`, `notifications.Example.json`, `README.md`, `roadmap.md`, and `START_HERE.txt` into the publish folder.
- **`-Zip`** — creates `SyntheticHttpMonitor-win-x64.zip` from that folder (use with **`-Package`** for a turnkey operator bundle).

Manual equivalent:

```powershell
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -o C:\Deploy\SyntheticHttpMonitor
```

## Manual install (no graphical installer)

If you prefer not to use **`SyntheticHttpMonitor.Installer.exe`**, copy the publish folder to the server, configure `appsettings.json`, then:

```cmd
sc.exe create "SyntheticHttpMonitor" binPath= "C:\Deploy\SyntheticHttpMonitor\SyntheticHttpMonitor.exe" start= auto DisplayName= "Synthetic HTTP Monitor"
sc.exe description "SyntheticHttpMonitor" "Synthetic HTTP/HTTPS monitoring with SMTP alerts."
```

Set the service **Log On** account in `services.msc`, then `sc.exe start SyntheticHttpMonitor`.

## Operations

| Action | Command |
|--------|---------|
| Start | `Start-Service -Name SyntheticHttpMonitor` or `sc.exe start SyntheticHttpMonitor` |
| Stop | `Stop-Service -Name SyntheticHttpMonitor` or `sc.exe stop SyntheticHttpMonitor` |
| Query | `sc.exe query SyntheticHttpMonitor` |

- **Logs:** Default file sink path is **`logs/synthetic-http-monitor-.log`** under the install folder (same folder as the EXE). The service creates the **`logs`** folder on startup if it is missing.
- **Config changes:** Edit JSON next to the EXE, then restart the service. If you use split files (`targets.json`, etc.), change those too.

## Versioning

All projects under this folder share **`Directory.Build.props`**. Each **`dotnet build`** / **`dotnet publish`** stamps **`FileVersion`**, **`AssemblyVersion`**, **`Version`**, and **`InformationalVersion`** as:

`{Major}.{Minor}.{Part3}.{Part4}`

| Part | Meaning |
|------|--------|
| **Major / Minor** | Product line. Edit **`SyntheticProductMajor`** and **`SyntheticProductMinor`** in `Directory.Build.props` when you intentionally ship a new product line (e.g. `2.0`). |
| **Part3** | `(Year − 2020) × 400 + DayOfYear` using the **build machine’s local calendar** (not a literal date string in the version). |
| **Part4** | **Minutes since local midnight** (0–1439) at build time. Must fit each **UInt16** segment of the Win32 file version. Builds in **different clock minutes** get a different Part4; **10–15 minutes apart** always differ. |

**Decode Part3:** `year = 2020 + (Part3 / 400)`, `dayOfYear = Part3 % 400` (1–366). Then map `dayOfYear` to a calendar date in that year.

**Decode Part4:** `hour = Part4 / 60`, `minute = Part4 % 60` (minutes since local midnight).

Example: **`1.0.2517.1080`** → Part3 `2517` → year `2020 + 6 = 2026`, day-of-year `117` (about late April); Part4 `1080` → `18:00` local (values depend on when you built).

**Programs and Features** / Setup’s version banner use **`SyntheticHttpMonitor.exe`**’s version info (same stamp on Config and Setup exes from the same publish folder).

**Note:** Two builds in the **same clock minute** share the same version; wait a minute or touch `Directory.Build.props` if you need a forced bump without changing code.

### Distribution (zip vs binaries)

- The **release zip** is the usual handoff: operators run **`SyntheticHttpMonitor.Installer.exe`** from the zip root; **`FileVersion`** on the shipped EXEs reflects **`Directory.Build.props`** at **publish** time on the build machine.
- Assemblies are **not** strong-named. **Authenticode** signing of the published EXEs is **optional**; use **`Publish-Monitor.ps1 -SignCertificateThumbprint`** (or **`-SignCertificatePath`**) when you package a build.

## Add/Remove Programs: effort vs alternatives

The graphical installer writes the standard **`HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\<ServiceName>`** keys and points **Uninstall** at **`SyntheticHttpMonitor.Installer.exe /uninstall`** (the on-disk name may still be **`SyntheticHttpMonitor.Setup.exe`** in older installs). No separate PowerShell install path is shipped in the package.

A full **MSI** (WiX, Advanced Installer, etc.) adds signing, repair, custom actions, and inventory integration, but is **more work** (typically hours to days the first time). Use that if your org requires MSI for Intune/SCCM or strict software catalog rules.

## Configuration

### Graphical editor (`SyntheticHttpMonitor.Config.exe`)

The config app edits **`appsettings.json`** in place. It loads the whole file, lets you change **SyntheticMonitor** (defaults, monitored URLs including per-row **Skip HTTPS cert**), **Alerting**, **Smtp**, **PagerDuty**, and **TicketingApi**, then saves those sections back while **keeping `Serilog` and any other top-level keys unchanged** (including if you use optional split JSON files for logging only — the editor does not edit `targets.json` / `notifications.json`; use a text editor for those if you split config). The editor preserves the legacy **`SyntheticMonitor:HttpClient`** JSON object when saving (for `DangerousAcceptAnyServerCertificate` until you remove it by hand). Use **Save changes** on the toolbar (or **File → Save changes**, **Ctrl+S**) when you are finished editing. If Windows blocks the write (common under **Program Files**), approve the **UAC** prompt so an elevated PowerShell step can copy the file into place. When you save the **installed** `appsettings.json` next to the service, the app also tries to **restart the Synthetic HTTP Monitor service** automatically (and warns if it cannot). The status bar shows whether that service is running. You can still use **Save as…** or run the whole editor as Administrator if you prefer.

### One file or several?

**Not foolish at all.** Splitting JSON is a common pattern: smaller files, clearer ownership (e.g. networking team edits targets, messaging team edits SMTP), and less merge pain in source control.

The host loads settings in this order (later sources **override** the same keys):

1. `appsettings.json`
2. `appsettings.{Environment}.json` (e.g. Development), if present
3. **`targets.json`** (optional) — typically `SyntheticMonitor` only (defaults + URLs to check)
4. **`logging.json`** (optional) — `Serilog` only
5. **`notifications.json`** (optional) — `Alerting`, `Smtp`, `PagerDuty`, `TicketingApi`

You can run with **only** `appsettings.json` (simplest). To split: copy the shipped examples and rename **without** `.Example`:

| Rename this | To this | Contents |
|-------------|---------|----------|
| `targets.Example.json` | `targets.json` | `SyntheticMonitor` |
| `logging.Example.json` | `logging.json` | `Serilog` |
| `notifications.Example.json` | `notifications.json` | `Alerting`, `Smtp`, `PagerDuty`, `TicketingApi` |

Then remove the matching sections from `appsettings.json` so you are not maintaining two copies (optional but recommended). If both define the same key, the **split file wins**.

### Reference (section keys)

| Section | Purpose |
|---------|---------|
| `SyntheticMonitor:Defaults` | Default interval, timeout, status codes, max body bytes, and optional `DangerousAcceptAnyServerCertificate` (used when a target omits its own value; combined with legacy `HttpClient` below). |
| `SyntheticMonitor:HttpClient` | **Legacy:** `DangerousAcceptAnyServerCertificate` still applies to targets that omit `DangerousAcceptAnyServerCertificate` (OR with `Defaults`). Prefer per-target settings under each `Targets` entry. **MITM risk** when bypass is in effect. |
| `SyntheticMonitor:Targets` | Per-URL checks; optional overrides. `DangerousAcceptAnyServerCertificate` on a target (optional): when `true`, that URL’s HTTPS probe skips server certificate validation. Omit or `null` to inherit from `Defaults` and/or legacy `HttpClient`. Omit or null `BodyRegex` to skip body matching. |
| `Alerting` | `FailureThreshold`, `RepeatWhileDownMinutes`, `SendRecoveryEmail`. |
| `Smtp` | Relay host, TLS, recipients, optional SMTP username/password. |
| `PagerDuty` | [Events API v2](https://developer.pagerduty.com/docs/ZG9jOjExMDI5NTgw-events-api-v2-overview) `trigger` when a target crosses the failure threshold; optional `resolve` on recovery (`dedup_key` per target). Set `RoutingKey` from your Events integration; `summary` includes target name, URL, and error text. |
| `TicketingApi` | Reserved; enabling it only logs that API ticketing is not implemented yet. |
| `Serilog` | Levels, sinks (file, console, Event Log), paths, retention. |

- **Monolithic example:** `appsettings.Example.json`
- **Split examples:** `targets.Example.json`, `logging.Example.json`, `notifications.Example.json`

Future direction and larger features (including **authenticated probes**) are sketched in **`roadmap.md`**.

## Release zip: what operators expect

Common patterns (lightest to heaviest):

| Approach | What the user sees | Tradeoff |
|----------|-------------------|----------|
| **Flat zip + obvious entry** | Many files; a **`START_HERE.txt`** (or `README.txt`) and clear README steps reduce hunting. | No extra tooling (this repo ships **`START_HERE.txt`** with **`-Package`**). |
| **Single top-level launcher** | e.g. **`Install.bat`** that starts **`SyntheticHttpMonitor.Installer.exe`** elevated, or only document that EXE in email/Intune description. | One more small file to maintain. |
| **Self-extracting archive (SFX)** | One downloaded `.exe` that unpacks to `%TEMP%` and runs Setup. | Built with **7-Zip SFX**, **WiX Burn**, **Inno Setup**, etc. |
| **MSI / MSIX / Win32 app** | One cataloged installer; Intune/SCCM friendly; repair and version rules. | WiX, Advanced Installer, or vendor packaging — more authoring time. |

Your idea — *“only the installer visible, it unpacks the rest”* — is exactly what **SFX** or **Burn/chained** installers do: one bootstrapper carries the payload (or downloads it). For a small self-contained folder, a **full SFX** is often enough; downloaders are more useful when the payload is huge or channel-specific.

## Optional: MSI-style installer

If your org standardizes on **MSI** or **Intune Win32** apps, you can wrap this folder with **WiX**, **Advanced Installer**, or similar using the same binaries and the same service **`sc create` / registry** steps the graphical installer performs, or translate those steps into the MSI tables.

## Troubleshooting

- **`dotnet restore` / NU1100:** Configure a NuGet source (`dotnet nuget list source`); add `https://api.nuget.org/v3/index.json` if you use the public gallery.
- **`Publish-Monitor.ps1` / MSB3030 / nested `publish\win-x64\...` path:** Use the script from the repo (it publishes each EXE to a staging folder, then merges). If you still publish by hand, do not point all three `dotnet publish` commands at the same `-o` folder without cleaning between runs — use separate output folders and copy together, or one staging folder per project.
- **`SyntheticHttpMonitor.Installer.exe` opens then exits with no window:** Older zips copied only the Setup **host** to the zip root while **`SyntheticHttpMonitor.Setup.dll`** stayed under **`Resources`** — the process cannot start. Rebuild with the current **`Publish-Monitor.ps1`**, which publishes Setup as a **single-file** app so the root **`Installer.exe`** is self-contained (it is larger than a split publish, by design).
- **`SyntheticHttpMonitor.Config.exe` asks to install .NET Desktop Runtime:** Run **`Resources\SyntheticHttpMonitor.Config.exe`** (not a copy at the install root). The config app shares the same self-contained runtime files as the service; the installer keeps those under **`Resources`** so the root folder stays tidy.
- **Service starts then stops:** Check logs; fix JSON, regex, or invalid URLs (`http`/`https` only).
- **HTTPS fails with certificate / TLS errors:** Fix the server certificate or PKI when possible. If you must probe trusted internal HTTPS with a broken chain or expiry, set **Skip HTTPS cert** for that row in the config app (or `DangerousAcceptAnyServerCertificate` on that target in JSON). Optional default under **Monitored URLs** applies when the row’s checkbox is unset (mixed). Legacy `SyntheticMonitor:HttpClient:DangerousAcceptAnyServerCertificate` still works for targets without an explicit value — understand the MITM tradeoff.
- **No mail:** Confirm `Smtp:Enabled`, relay connectivity, and `To` / `From`.
- **`New-Service` / credential errors:** Use **Cancel** at the credential prompt and set **Log On** manually in `services.msc` (account needs **Log on as a service**).
- **Config app / UAC / service restart:** If you cancel the UAC prompt, the save to a protected folder is aborted. Automatic **service restart** only runs when the file you saved is the service’s **`appsettings.json`** in its install directory (not when using **Save as…** to another path). If restart still fails, use **services.msc** or an elevated console.
