# Synthetic HTTP Monitor — improvement roadmap

This file captures **directional** plans: priorities change with operational need. Treat it as a backlog sketch, not a commitment schedule.

---

## High priority — checklist (impact first)

Check these off in this file when done (single source of truth for “nailed it”).

| Done | Item | Impact | Notes |
|:----:|------|--------|--------|
| [x] | **MailKit / dependency hygiene** | Clears **NU1902** / [GHSA-9j88-vvj5-vhgr](https://github.com/advisories/GHSA-9j88-vvj5-vhgr) (moderate: STARTTLS response handling / SASL downgrade risk). | Worker `SyntheticHttpMonitor.csproj` uses **MailKit ≥ 4.16.0**. Verify locally: `dotnet list SyntheticHttpMonitor.sln package --vulnerable --include-transitive`. |
| [x] | **CI for `SyntheticHttpMonitor.sln`** | Every push/PR gets a **Release** build on **windows-latest** (required for `net10.0-windows`). | **GitHub:** **`.github/workflows/build.yml`** (also runs `dotnet list … --vulnerable`). Optional later: publish artifact zip (same layout as `Publish-Monitor.ps1`). |

---

## Recently shipped (reference)

- **GUI installer** (`SyntheticHttpMonitor.Setup.exe`) and **config editor** (`SyntheticHttpMonitor.Config.exe`).
- **Per-target TLS bypass** — `SyntheticMonitor:Targets[]:DangerousAcceptAnyServerCertificate` and `Defaults` (config app: **Skip HTTPS cert** column + default checkbox). Legacy global `SyntheticMonitor:HttpClient:DangerousAcceptAnyServerCertificate` still merged for targets without an explicit value (see README).
- **Setup version banner** — shows package vs installed `SyntheticHttpMonitor.exe` version and logs before/after versions on upgrade (see README “Upgrading vs fresh install”).
- **Build-stamped file version** — `Directory.Build.props` encodes local calendar date + time-of-day into `FileVersion` parts 3–4 (see README **Versioning**); bump `SyntheticProductMajor` / `SyntheticProductMinor` only for intentional product-line changes.
- **Publish-time Authenticode** — `Publish-Monitor.ps1` signs the three EXEs with **`signtool.exe`** when the Windows SDK is installed, otherwise **`Set-AuthenticodeSignature`** (same as `.ps1` signing); see README **Code signing**.

---

## Near-term ideas (smaller scope)

| Idea | Notes |
|------|--------|
| **MailKit / dependency hygiene** | Tracked in **High priority — checklist** above (check off there when upgrading). |
| **Custom HTTP verbs / headers** | `GET` only today; optional `HEAD`, static headers (API keys, `Accept`), or small POST body for health probes. |
| **Probe result metrics** | Optional Prometheus / OpenTelemetry counters (success, latency, status class) for dashboards. |
| **Structured “reason” in alerts** | Richer email body: last N failures, timing breakdown, TLS vs HTTP vs regex failure. |
| **CI** | Tracked in **High priority — checklist** above; optional follow-up: artifact zip matching `Publish-Monitor.ps1`. |

---

## Initiative estimates — authenticated probes & OAuth mail (backlog sizing)

Rough **level of effort**, **likelihood of success** (honest ranges; unknowns are IdP / tenant policy and security review), and a **short plan** for two asks. These are **not** commitments—use them for prioritization only.

### 1. Send credentials to websites and **verify** they work

| | |
|--|--|
| **Level of effort** | **Medium–large** if bundled with first-class **auth on probes** (headers, Basic, bearer, optional OAuth2 client credentials)—see **Authentication for monitored URLs** below. A **dedicated “verify” action** on top of that is **small–medium** (reuses the same `HttpClient`/handler path, one-off request, clear UI + logging, redacted output). **Order of magnitude:** ~**2–4 engineer-weeks** for “static/Basic/bearer + in-app test + docs” on a codebase this size; **more** if you add named credential profiles, vault integration, or OAuth2 token refresh in the same release. |
| **% likely success** | **~75–85%** for **machine-oriented** auth (API keys, Basic, bearer, client-credentials) plus a **deterministic** success signal (HTTP status, JSON body, or known “who am I” endpoint). **~55–70%** if “verify” must mean **full interactive web login** (forms, CSRF, MFA, anti-bot)—that is usually the wrong tool for a headless Windows service unless scope is narrowed (e.g. only a token endpoint or a dedicated health URL). |
| **High-level plan** | (1) Extend probe pipeline so each target can attach **resolved credentials** (see existing roadmap section). (2) Define **what “verified” means** (e.g. `200` on `GET /me`, or body contains marker)—same as probe success rules today. (3) **Config app / CLI:** “Test authentication” runs **one** probe with current JSON (no schedule), shows result + **redacted** response preview. (4) **Security:** never log secrets; optional DPAPI or external secret file. (5) **Tests:** mock HTTP server + one integration test with real token from a dev tenant. |

### 2. **OAuth-style** email (Gmail, Microsoft 365, etc.)

| | |
|--|--|
| **Level of effort** | **Medium** for a **single provider** (e.g. Gmail SMTP + **refresh token** already obtained out-of-band) using **MailKit**’s OAuth2 support, plus **encrypted** token storage and refresh logic: **~1–2 engineer-weeks**. **Medium–large** (**3–5+ weeks**) for a **multi-provider** story (Google + Microsoft + “generic OIDC”), polished config UI, token rotation UX, and org security review. Google / Microsoft **app registration**, consent branding, and **restricted scopes** reviews are calendar risk outside pure coding time. |
| **% likely success** | **~80–90%** for **“bring a refresh token / client id from your cloud console, service stores and refreshes it”** on Gmail or M365, assuming the org allows SMTP/OAuth and a human does the one-time consent. **~50–65%** if the requirement is **zero admin setup** and fully **interactive “Sign in with Google” inside the config app** on every install, with no pre-registered client—doable (PKCE, loopback or device code) but more moving parts and policy edge cases. |
| **High-level plan** | (1) Extend `SmtpOptions` with **auth mode** (`password` vs `oauth2`) + client id, tenant/scopes as needed, **refresh token** reference (value in DPAPI file or user secret store—not plain `appsettings.json`). (2) Implement **token cache** + refresh on 401/expiry before send. (3) **MailKit:** `Authenticate` with OAuth2 SASL (document exact Gmail vs M365 host/ports). (4) **Bootstrap UX:** document “generate refresh token once” (Google Cloud Console / `az` / PowerShell script) or add a **small helper tool** for consent; optional later: embedded browser sign-in. (5) **README + security:** least privilege scopes (`https://mail.google.com/` or Graph send scope), rotation, and failure alerting when refresh fails. |

---

## Authentication for monitored URLs (larger initiative)

Today the monitor performs **anonymous GET** requests. Adding **authentication** is feasible but touches configuration, secrets, token lifecycle, security review, and UI. Below is a **high-level** breakdown of what it would take, in roughly logical order.

### 1. Requirements and threat model

- Decide which flows matter first: **static API key / bearer header**, **Basic auth**, **OAuth2 client credentials**, **mutual TLS (client cert)**, or **interactive login** (usually a poor fit for a headless service).
- Document **what must never** appear in logs, events, or crash dumps (passwords, refresh tokens).
- Clarify **per-target vs global** credentials (most teams want per-target or named “credential profiles” reused by several URLs).

### 2. Configuration model

- Extend `TargetOptions` (and/or introduce `CredentialRef` / named profiles in JSON).
- Split **non-secret** config (header names, OAuth authority URL, client id, scopes) from **secrets** (client secret, API key, passwords).
- Prefer **Windows DPAPI**, **optional file path** outside repo, or integration with your org’s vault — storing long-lived secrets only in `appsettings.json` is a common anti-pattern.

### 3. Runtime plumbing in `HttpProbe`

- Build `HttpRequestMessage` with **Authorization** (or custom headers) from resolved credentials.
- For **OAuth2 client credentials**: HTTP call to token endpoint, parse JSON, cache **access_token** with **expiry minus skew**, refresh in a thread-safe way when multiple targets share one identity.
- For **Basic**: base64-encode `user:password` (clearly document encoding and TLS requirement).
- For **mTLS**: load client certificate from store or PFX path, attach to `SocketsHttpHandler` (may require **separate `HttpClient` / handler per credential** if certs differ per target).

### 4. OAuth2 and edge cases (if you go beyond static headers)

- Discovery vs fixed token URL; clock skew; **429/5xx** backoff on token endpoint.
- Optional **certificate-bound** tokens or MTLS at token layer if your IdP requires it.
- Regression tests with a mock IdP (e.g. WireMock, Testcontainers) so CI does not hit real tenants.

### 5. UX: config editor and docs

- New **“Authentication”** section or tab: pick auth type, bind to profile, mask secrets, “test connection” optional.
- README: how to rotate secrets, least privilege, and **never** commit secrets.

### 6. Operational hardening

- **Least privilege** service account; file ACLs for secret files.
- Alerting when token refresh fails repeatedly.
- Optional **redacted** logging mode for troubleshooting without leaking credentials.

### 7. Effort (very rough)

| Scope | Order of magnitude |
|--------|-------------------|
| Static headers / bearer token from config (no refresh) | Small (days) if secrets handled acceptably |
| Basic auth + header presets | Small |
| OAuth2 client credentials + in-memory cache + tests | Medium (1–2 weeks) depending on IdP quirks |
| Named credential profiles + secret store integration + UI | Medium–large |
| mTLS + multiple handlers | Medium (per-target handler or client factory design) |
| Full “enterprise” vault + rotation + audit | Large (multi-sprint), usually pairs with platform standards |

---

## TLS certificate monitoring

Today the monitor performs HTTP-level checks only. Adding **TLS certificate monitoring** would cover connectivity and cert health for Cloudflare-protected sites (where HTTP probes get blocked) and provide cert expiry alerting for any HTTPS endpoint.

**Design note:** cert expiry and uptime monitoring have fundamentally different polling cadences — weekly is fine for cert checks, whereas uptime needs seconds-to-minutes. `IntervalSeconds` is already per-target so the scheduler handles this without changes. The meaningful difference is alerting semantics: cert expiry is a countdown (warn at 30 days, critical at 7 days) rather than binary down/recovered. That logic belongs in a dedicated probe class and a new config section, keeping the existing uptime path clean.

| Scope | Notes |
|-------|-------|
| **TCP connect check** | `TcpClient.ConnectAsync(host, 443)` with timeout — proves network reachability regardless of Cloudflare HTTP blocking. Small effort. |
| **TLS handshake + cert read** | `SslStream` over the TCP connection; read `RemoteCertificate` as `X509Certificate2`. Returns subject, issuer, SANs, `NotAfter`. Works through Cloudflare (TLS handshake precedes HTTP). Medium effort. |
| **Cert expiry alerting** | New `CertificateExpiryDaysWarning` / `CertificateExpiryDaysCritical` thresholds per target; escalating severity through existing SMTP + PagerDuty channels. Medium effort on top of TLS read. |
| **New target type** | `"Type": "CertificateExpiry"` (or optional flag on existing targets) — skips HTTP probe entirely, runs TCP+TLS check only. |

**Planned after** HTTP monitor is rounded out (OAuth email, custom headers, etc.).

---

## Ticketing API

`TicketingApi` is reserved in config today; **implementing** real ticket creation would be a separate track (payload schema, idempotency, retries, correlation with probe failures).

---

## MSI / enterprise packaging

WiX / Intune / SCCM packaging is still optional; the Setup `.exe` + zip path remains the lightweight default until org policy requires MSI.
