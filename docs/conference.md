# Browser and protocol control walkthrough

These opt-in browser and protocol controls demonstrate real token theft, sender constraints, and downstream exchange. Presentation narration and rehearsal records are maintained separately. Commands below operate only on dedicated reference fixtures.

## Setup

Use a compatible ProAuth environment and the [dependency setup](development.md#proauth-dependency). `config/product-build.json` identifies the exact package release. The disposable browser BFF also requires `ProAuth.Oidc.Client.InMemory` and `ProAuth.Oidc.Client.AspNetCore`.

The five lab hostnames use the Traefik entry point `mervikon-lab`. Publish its HTTPS listener only on the operator workstation’s loopback interface and configure the five lab origins (including their port) and registered redirects to match. For an existing isolated proxy whose HTTPS listener is already restricted to the operator, set `MERVIKON_LAB_ENTRYPOINT=websecure` in the private environment. A hostname ending in `localtest.me` does not limit who can send a matching Host header.

From this repository, with the existing private `.local/local.env`:

```sh
node scripts/mervikon.mjs configure-workflows
node scripts/mervikon.mjs configure-lab
node scripts/mervikon.mjs configure-protocols
node scripts/mervikon.mjs check --lab --protocols
node scripts/mervikon.mjs build-cli
node scripts/mervikon.mjs provision --lab --protocols
node scripts/mervikon.mjs build --lab
node scripts/mervikon.mjs compose --conference --protocols -- \
  --profile conference --profile demo --profile protocols up -d --no-build \
  workplace-bff procurement-api fulfilment-api finance-bff demo-console protocol-api \
  conference-spa conference-bff conference-api conference-wrong conference-attacker
node scripts/conference.mjs preflight
```

Each command accepts `--env-file /absolute/path/private.env`. Existing identities, keys and secrets are retained. Setup requires trusted HTTPS for the issuer and all application names, the shared proxy's current exact IP, and the existing protocol tenant router/mTLS middleware. The conference overlay is a **local opt-in lab**, not a public deployment profile. Do not use `--remove-orphans` against other optional reference profiles. `conference.mjs up` starts only the viewer and the five browser lab services; the full command above also enables the Workplace/Procurement evidence controls.

Preflight checks TLS, exact discovery issuer, five lab health endpoints and anonymous API rejection. `infrastructureReady` does **not** certify the authenticated flow. Rehearse with real credentials; the UI shows only redacted token previews and fingerprints, never complete credentials. Complete first-use password changes, actual presenter passkey enrollment and subject-to-membership mapping in advance. Use separate dedicated browser profiles for the conference lab and each Workplace actor to avoid stale cross-tenant SSO context. Existing private credentials are the source of truth; no shared password is documented here.

## Tabs and fixtures

| Surface | Default URL / command | Fixture |
| --- | --- | --- |
| Live evidence | `https://democonsole.localtest.me`, **Protocol Evidence** | Up to 500 sanitized observations; run selector and previous/next |
| Workplace | `https://workplace.localtest.me` | Elena requests; separate Amir browser approves |
| Direct SPA | `https://securitylab-spa.localtest.me` | Real public OAuth code/S256 client; localStorage tokens intentionally exposed |
| Browser BFF | `https://securitylab-bff.localtest.me` | Same UI and quote, real package token/ticket stores, HttpOnly session |
| Finance | `https://finance.localtest.me` | Real FAPI-configured client, optional baseline login |
| Sender comparison | `node scripts/mervikon.mjs lab-run --lab` | Real TLS capture, separate attacker runner, bearer vs DPoP |
| mTLS | `node scripts/mervikon.mjs protocol-run --variant mtls` | Actual TLS certificate binding; controlled token possession |

Prepare a CHF 18,500 Industrial Laser Calibration Station purchase as Elena and approve it as Amir. Keep its actual GUID/revision visible. This is real business authorization with separation of duties, not a seeded approved row. Use a new purchase for a fresh run. Use [API assurance](api-assurance.md) for higher-value approvals and payment release.

## Controls

1. **Baseline.** Sign into Workplace as **Elena** with her passkey; the top-right account name comes from `/bff/user`. Repeatable recordings use her registered virtual authenticator, not a physical-device prompt. Pause on the Console's fresh event list before opening the callback issuer/verifier and access-token audience. PAR may move S256 to the back channel. In **Security Lab SPA**, orient the audience to regular actions, security experiments, action results and the token panel. Redacted values and SHA-256 fingerprints show actual token changes. Read quote (200), Wrong audience (401), Rotate refresh token (200, distinct fingerprints), then Check access token (introspection active and API 200). **Replay old refresh token** sends only the old value; **Try current refresh token** is a separate legitimate-client check of the successor. Both should return `invalid_grant`. **Check access token** again compares AS activity with local API validation for the same fingerprint; report the observed values separately. The server-only introspection helper uses the existing confidential lab client's resource authorization and never exposes its secret in `/lab/config`. Reauthenticate before the later theft demo. The SPA is OAuth-only and does not use an unvalidated ID token as application identity.
2. **Sender constraint.** Start `lab-run --lab`; inspect its live observations while it performs its own expiry/cleanup. Legitimate requests succeed before/after replay. Stolen bearer succeeds; stolen DPoP token and copied proof fail. Inspect actual `cnf.jkt`, `htm`, `htu`, `ath` and hashed `jti`. Run mTLS separately: compare `cnf.x5t#S256` with the presented certificate thumbprint; missing, different and forged-header controls return 401. mTLS uses controlled possession of the actual issued token, not a fabricated TLS interception. These commands wait for expiry (roughly two minutes combined sender run, one minute mTLS); narrate the already-arriving results while cleanup completes. Never kill them just to save stage time.
3. **Browser boundary.** Fresh SPA sign-in → Read quote 200 → Load compromised widget. The script really reads a token and posts it to the fixed attacker service; the service independently calls the quote API (200). In the BFF, Sign in → Read quote 200 → same widget: no OAuth token found, no independent token replay, **same-origin request still 200** using the real session. Do not call a 401/502 positive control a successful BFF defense. Both variants deliberately permit this compromised script; this comparison isolates token placement, not CSP or complete XSS prevention.
4. **Exchange.** In Amir’s Workplace session, turn on **Demo mode** in the top toolbar. Under **Approved purchases**, choose **Try original token** (expected HTTP 401). The original holder signs a fresh proof for the fixed downstream URL, avoiding a wrong-key confounder. In Amir's approved purchase select **Hand to supplier**. With the conference overlay, Procurement first requests disallowed `fulfilment.write` (actual `invalid_scope`), then performs the permitted exchange for `fulfilment.create`. Compare input/output audience, scope, hashed subject/actor and changed `jkt`. Fulfilment validates the new sender-bound token and expected actor and imports the approved order. This is Workplace → Procurement → Fulfilment; No original proof/private key is forwarded and no machine-token fallback exists.

Decoded fields explain messages. Server responses establish observed acceptance/rejection. An HTTP 401 alone does not prove which validator rejected a request. Browser-origin reports are labelled client reports, not independent server verification. Runs correlate within a producer/operation; OIDC back-channel spans and the later business action can have separate IDs. Keep **All runs** selected when comparing those steps.

## Optional, runnable extensions

```sh
node scripts/conference.mjs nonce
node scripts/conference.mjs fapi
```

Nonce: dedicated DPoP client requests a real token without a nonce → HTTP 400 `use_dpop_nonce` and nonce header → fresh proof with returned nonce → token 200 → protected API 200. Raw nonce/token/proof values are excluded from evidence. This demonstrates token-endpoint nonce negotiation; it is not an API nonce/cluster test. The ephemeral key dies with the probe; the short-lived token expires.

FAPI: actual Finance registration and PS256 `private_key_jwt`; valid PAR 201; omitted PKCE and `plain` PKCE 400 `invalid_request`; authorization without PAR redirects to the registered callback with `invalid_request`. PAR without DPoP is **accepted (201)** by the current endpoint. That does not show bearer token issuance. This bounded guardrail probe does not claim JAR/JARM, a full authorization-code binding-negative matrix, payment assurance, or FAPI certification. The ephemeral PAR requests expire without completing authorization.

## Replay, reset and failure handling

```sh
# Before restarting the Console, retain its current safe observations:
node scripts/conference.mjs recording
# Recreate only disposable browser-lab processes:
node scripts/conference.mjs reset
```

Use `recording --run ID[,ID]` for a focused set of observed runs; unknown IDs fail rather than creating an empty success story. Without that option the full feed includes debugging failures.

`recording` writes `.local/evidence/conference-recording.html`, a standalone offline previous/next viewer with original timestamps and package version. It cannot issue protocol requests. Console also exports/imports inert JSON; imported mode stops live polling. Export before Console restarts (the feed is in memory) and copy each desired recording to a distinct filename before a later export overwrites the default. Open the HTML once with the browser offline during rehearsal. Never export browser HARs, private CLI logs or raw captures as the fallback.

Before reset, use **Clear this browser fixture** in the SPA: process actual revocation responses, then remove mervikon-owned local/session storage. Self-contained access tokens can remain accepted until expiry. A BFF browser clear is not server sign-out; use package logout or the isolated process reset, which discards the lab's in-memory stores. Product SSO remains, so an explicit sign-in can authorize again. Process reset does not revoke all AS grants. The script preserves normal business APIs, product identities, reference Redis and enrolled passkeys. Recreate business fixtures through actual UI/API actions; never reset the shared product SQL container to clear a purchase.

If a positive control fails, stop interpreting later rejections as security evidence. Retry once after checking health/login/expiry; otherwise switch to the labelled recorded run. Keep backup real credentials/passkeys private, and do not silently substitute a password login while narrating a passkey ceremony.

## Security Lab names

New local configurations use `securitylab-spa`, `securitylab-bff`, `securitylab-api`, `securitylab-wrong` and `securitylab-attacker` hostnames. Deployment service names, `CONFERENCE_*` environment keys, stable registration IDs, scope and audience identifiers remain compatible with existing automation. For an existing environment, update the five `CONFERENCE_*_HOST` / `CONFERENCE_*_ORIGIN` pairs together, re-import `config/proauth/unsafe-lab/91-conference.yml` for the matching registered redirects, and recreate only the five lab services. `configure-workflows` preserves existing settings and will not silently migrate them. Workplace is rebuilt separately for its account header.

## Guided sender-constraint execution

Run `node scripts/sender-demo.mjs` and open `http://127.0.0.1:5091`. This explicit local operator starts only the isolated capture lab and offers fixed bearer, DPoP and mTLS sequences. For each variant: **Call API legitimately**, inspect the issued token, **Obtain the stolen token**, **Replay the stolen token**, then inspect the binding evidence. All HTTP outcomes are returned by the running probes. The UI shows run IDs, observation times and projected fields; raw credentials never reach it. The existing non-interactive `lab-run` and `protocol-run` commands remain unchanged unless `DemoMode=true` is explicitly supplied to a probe.

Bearer/DPoP artifacts come from the real compromised TLS terminator and are read by the existing independent runner. mTLS deliberately supplies the real token to a new HTTP request without a client certificate; it does not claim TLS interception or process-level isolation from the legitimate key. The successful request's public certificate thumbprint is retained for comparison. DPoP token-only and copied-proof replay have separate result rows.

The operator binds only `127.0.0.1`, validates Host and same-origin CSRF-protected commands, accepts a fixed action sequence, and uses the existing lab lock. It does not expose arbitrary commands. Demo Console remains read-only, without a Docker socket or command endpoint. The operator requires local Docker access; run it only on the intended training workstation. Ctrl+C drains pending fixed steps and waits for cleanup before stopping the isolated lab. Do not force-kill it. If a live explanation exceeds the short token lifetime, stop interpreting later failures as sender-constraint evidence and start a fresh run after cleanup.

For the final **Token validation & replay state** review, run `protocol-run --variant jwt`, `--variant opaque`, `--variant jwe` and `replica-run --lab` before starting the guided operator. These are completed observations, not requests caused by opening the Console table. The guided mTLS run updates that table with its fresh result after cleanup. Guided bearer/DPoP runs also update the existing comparison page, which remains useful for workshops.

Console navigation uses **Protocol Evidence**. Its header exports the **live protocol feed**; the view's **Export this recording** exports the selected recording, including imported evidence.


### Browser boundary rehearsal

Demo 3 uses `securitylab-spa.localtest.me` and `securitylab-bff.localtest.me`. The BFF shares the SPA's regular-action/security-experiment grouping, hides SPA-only token operations, and displays authentication state from the package `/bff/user` endpoint (and the name when the endpoint supplies it). Its **What JavaScript can read** panel takes a real snapshot of local/session storage key names, `document.cookie` names, and the two known lab OAuth-token slots. **Inspect JavaScript access** refreshes that snapshot. Values are not rendered. This is scoped observation of this application, not an exhaustive scan of browser memory; JavaScript cannot itself prove the existence or HttpOnly flag of an unreadable cookie. Recording assertions separately verify the actual BFF cookie's HttpOnly/Secure flags through browser automation.

Establish a successful quote first, inspect the browser boundary, and then execute the same compromised widget in each variant. In the SPA the collector receives a real token and independently replays it successfully. In the BFF no token is found by this probe and no independent token replay is performed, while the same-origin authenticated quote still returns 200. Do not call this XSS prevention: the injected script can still act through the session and read returned data.

### Workplace handoff presentation

After standard `npm start`, the presenter switch is hidden. To enable the audience comparison on an existing walkthrough, apply the conference overlay to Workplace alone:

```sh
node scripts/mervikon.mjs compose --conference -- up -d --no-deps workplace-bff
```

Refresh Workplace and use Amir’s profile. The Procurement API retains the current purchases. Its optional scope-denial demonstration requires its own conference configuration.

For Demo 4, enable **Demo mode** in Workplace's top account toolbar. It starts off and is offered only when `conferenceControls` is enabled by the server. Each approved purchase an approver can hand off gains **Try original token** beside **Hand to supplier**, with two prominent result panels directly below the row. The original-token panel reports Fulfilment's actual `downstreamStatus` from the fixed `GET /api/orders` probe; the handoff panel reports the actual browser-to-Procurement response and confirms order acceptance only after success. Both observations remain visible for comparison. This is a UI toggle, not an authorization bypass; the probe does not mutate the selected purchase. Endpoint/transport failures are displayed locally as errors, and a failed test-endpoint request is not labelled a Fulfilment rejection. Outside demo mode the normal handoff still shows its result beside the purchase.

### Approval feedback beside the purchase

Demo 5 uses the same local result treatment as the handoff: the approval response, five-minute assurance requirement and **Verify before approving** action appear directly beneath the selected purchase. Only a recognized `insufficient_user_authentication` challenge offers verification; generic failures remain errors in that purchase's panel. Verification still uses the existing explanation page and named BFF policy, returns without submitting the action, and requires an explicit second **Approve**. That request displays its actual HTTP response and updates the row from the API's returned purchase. The normal view gets this feedback without enabling Demo mode.
