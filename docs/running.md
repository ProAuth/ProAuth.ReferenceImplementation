# Running the reference implementation

The applications run locally and use your ProAuth test installation for sign-in and token issuance. The standard configuration uses Enterprise custom claim rules and branded sign-in views. Building the applications and provisioning CLI also requires customer package-feed credentials. To request an evaluation with these features, [contact ProAuth](https://www.proauth.net/contact/).

## Connect your ProAuth installation

For a **fresh clone**, start from the repository root with Node 22.12 or newer, Docker Compose 2.24.4 or newer, and a ProAuth 3.4.0 test installation you administer. For an **existing Mervikon environment**, use its current private environment and skip initialization. For a checkout from before the application naming change, follow [the rename migration](#migrating-an-earlier-deployment) first. See [refreshing demo memberships](#business-membership-and-the-first-journey) if sign-in already works.

For an existing deployment moving to 3.4.0, coordinate replacement of each BFF’s replicas and expect a fresh sign-in. If you run the optional replica lab, drain its old proof window before switching stores. See [BFF session migration](bff-replicas.md) and [replay store migration](replica-controls.md).

```sh
npm start
```

On a fresh clone the script installs the locked Node dependencies, creates a private `.local/local.env`, a bootstrap credential template and a customer NuGet config template, then pauses for your installation settings. It generates a Workplace secret and an initial training password and fills in the fixture IDs. With an existing configuration, `npm start` goes straight to setup and startup. To select a different environment file, use `npm start -- --env-file /path/to/local.env`.

| Where | What to supply |
| --- | --- |
| `.local/nuget.customer.config` | Your ProAuth customer feed username and password. |
| `.local/bootstrap.env` | The provisioning client secret. Set the corresponding `BOOTSTRAP_CLIENT_ID` in `.local/local.env`. |
| `.local/local.env` | Set `PROAUTH_HOST` to the ProAuth HTTPS address and `PROAUTH_ISSUER` to the exact issuer in its discovery document. It must be reachable from the host and the provisioning container. |
| TLS | Set absolute paths for `TRUSTED_CA_FILE`, `TLS_CERT_FILE` and `TLS_KEY_FILE`. Trust the CA in your browser. The certificate must cover the chosen application hosts, including `democonsole.localtest.me` with the defaults. |
| UserStores | Set the three `MERVIKON_*_CONNECTION_STRING` values to database connections reachable by **ProAuth**, with the dedicated database names `mervikon_manufacturing`, `mervikon_research` and `mervikon_partners`. For a server-configured ProAuth alias, use `ConnectionStringAlias=YOUR_ALIAS;Database=mervikon_manufacturing` and the corresponding catalog for each other store. |

The default customer setup starts its own loopback HTTPS proxy and needs local port 443 free. If your ProAuth Docker proxy already owns port 443, set `PROXY_MODE=shared`, `MERVIKON_EDGE_NETWORK` to its existing network and `TRUSTED_PROXY_IP` to the proxy's exact address there. Use `docker network inspect <network-name>` to find the proxy's address on that network. Set `SHARED_PROXY_CONTAINER` when its container name differs from `proxy`. Complete hostname changes before generating workflow settings.

```sh
npm start
```

`provision` imports the fictional accounts and configures both human and machine assignments for the purchase walkthrough. A successful smoke check includes an anonymous purchase request returning 401. Open [Workplace](https://workplace.localtest.me) and sign in as **elena** through **Manufacturing**. For a newly created account use the private `INITIAL_USER_PASSWORD`, then change it when prompted. Existing accounts use their current password. Open [Demo Console](https://democonsole.localtest.me) alongside Workplace.

Continue with the [purchase walkthrough](walkthrough-local.md) for the five actors, device code, and payment verification. Then try the [high-value approval](api-assurance.md) and [token representation and certificate controls](protocol-controls.md). The optional protocol controls use the shared local proxy described in their runbook.

## Build and provisioning details

The shared proxy forwards requests to the reference containers. If its address changes, update `TRUSTED_PROXY_IP` and recreate the affected reference services. Reference Redis uses a private network and a persistent volume.

`build` deliberately uses Docker directly with explicit BuildKit secret mounts. Compose 5's Bake integration may reject secrets outside the repository unless filesystem entitlements are supplied. NuGet credentials are mounted during restore only. The public CA is installed in the image's trust store; rebuild when changing that CA. No TLS bypass is enabled.

Provisioning passes the bootstrap secret and initial password through a short-lived private CLI response file. The pinned parser accepts single-line values without double quotes. Use the generated initial password or a compatible high-entropy value. The Linux CLI container uses an isolated `.local/cli-container` profile and encrypted token store. It mounts only configuration, its private state and the public CA. Applications receive only their own runtime settings, never the bootstrap secret file.

The 3.4.0 CLI reconciles options by owner and name across all result pages. The manifests keep their existing explicit IDs. If you are upgrading a ProAuth installation that used an older importer, follow the ProAuth operations guide *Prepare option collections for an upgrade* before deploying its `OptionOwnerUniqueness` migration. The CLI reports ambiguous duplicates for operator review.

The Procurement API supplies ProAuth's `IssuerBoundConfigurationManager` and `IssuerBoundConfigurationRetriever` to the resource-server package. Setting `Authority` and `Audience` alone does not load signing keys in the tested package. Discovery validates the exact configured issuer before retrieving keys, and transport uses normal certificate validation.

The reference scripts do not reset the external identity service. Follow its storage and recovery requirements independently. After an identity-service reset, re-provision fixtures, repeat enrollment and verify subject mappings; preserving `.local` alone does not preserve identities.

## Dedicated identity resources

`config/proauth/ownership.json` records fixed IDs for this reference customer, subscription, tenants, providers, clients, users and child records. Do not regenerate it for an environment restart or company rename. The offline ownership check rejects references outside that set and generated GUID/secret placeholders. Product CLI schema validation remains a separate online check.

The manifests are separated into base organizations/providers, client registrations, disclosure policy and user fixtures. Manufacturing's local account provider enables the initial local slice; it is not advertised as corporate federation. Research and partners have their own UserStores. Provisioning checks the database name in each connection string against the dedicated names listed above.

The existing root bootstrap credential creates the dedicated customer; this does not grant root privileges to an application. Subsequent customer-scoped administration still needs the scoped provisioning identity gate. CLI logs are private and can contain sensitive diagnostics; do not publish them. No cleanup command deletes customers or shared infrastructure.

The trusted-context disclosure rule selects `pa_iss_tenant` and `pa_iss_clientapp` from trusted issuer context for access tokens within the dedicated reference subscription. A client-scoped rule with a deferred relationship was rejected at create time by the tested API/CLI combination. The supported subscription assignment avoids that dependency without touching another subscription.

## Business membership and the first journey

The normal `provision` command prepares the five walkthrough actors and three workloads. Purchasing owns Elena's requester role and Amir's approval limit. ProAuth owns the identity of the person signing in. The setup joins the two using the fixed UserStore and user IDs in the imported YAML. It reads the synchronized ProAuth account in the demo subscription and uses its public subject, the exact issuer and the fixture tenant. The [demo assignments](../config/business/demo-members.json) describe who can do what.

**Already signed in as Elena and seeing a missing assignment?** Check that you selected the Manufacturing training provider. After updating this checkout, run:

```sh
node scripts/mervikon.mjs build-cli
node scripts/mervikon.mjs setup-memberships
```

Reload Workplace. This repairs assignments for the current installation without importing users, resetting passwords, or restarting an API. An unresolved or conflicting account stops the update before the file is replaced. A rerun preserves existing role and partition limits and fills missing demo rows. Use this explicit repair only when those grants are intended. Ordinary provisioning seeds a new membership file once and preserves an existing file, including deleted people and workloads.

The APIs mount the dedicated `ENTITLEMENT_DIR` read-only. If upgrading from the old single-file mount, copy the existing `memberships.json` into a new dedicated directory, set its absolute path in the private env, and recreate the three APIs once to adopt the mount. API recreation clears in-memory purchases. Later membership changes take effect on the next request. For your own accounts or pairwise subject registrations use the [manual assignment procedure](authorization.md#assignments-and-updates).

## Changing environments

For a separate ProAuth installation, choose a new private directory for its credentials, keys, and evidence. On first use, the following command creates the environment file there. The customer NuGet config stays at `.local/nuget.customer.config` in the checkout.

```sh
npm start -- --env-file /absolute/private/path/hosted/local.env
```

For a hosted issuer, keep the generated `PROXY_MODE=standalone` setting. Fill in your hosted ProAuth address, exact discovery issuer, provisioning credentials, database connections, and TLS paths as described [above](#connect-your-proauth-installation). Run the same command again to provision and start the reference. The local proxy binds to loopback port 443 and routes the seven application hosts using its file configuration. Keep port 443 free and use a certificate trusted by the browser and the reference containers.

## Kubernetes

The Helm chart in `deploy/helm/mervikon` covers the initial Workplace and Procurement slice. Build/push the reference images to your chosen registry, create the application client-secret, membership and TLS Secrets in a dedicated namespace, and supply the exact issuer, IDs, ingress address and application hosts through a private values file. The application Secret contains `client-secret`, `workplace-dpop-jwk`, `data-protection-certificate` (base64 PFX) and `data-protection-password`. Preserve the Data Protection certificate across restarts. Configure DNS, certificate trust, ingress reachability from the BFF, and mervikon-only provisioning before deployment. Provisioning is an explicit CLI operation, never a chart hook racing application startup.

```sh
helm lint deploy/helm/mervikon -f /private/path/values.yaml
helm template mervikon deploy/helm/mervikon -f /private/path/values.yaml
# Only after selecting and verifying your intended Kubernetes context:
helm upgrade --install mervikon deploy/helm/mervikon \
  --namespace mervikon --create-namespace -f /private/path/values.yaml
```

The chart keeps one instance of each business API, private persistent Redis and an ingress policy for Redis allowing only Workplace. The cluster must enforce NetworkPolicy. Public visitor isolation, replica tests and FAPI transport checks remain separate gates; a chart render does not establish them.

## Refresh an existing rehearsal environment

A source build does not update an already running container. Rebuild the reference images, then recreate the application containers with the **same environment file and Compose overlays** used for the rehearsal. Inspect the existing container's `com.docker.compose.project.config_files` label if you are unsure which overlays are active. Do not print or export its full environment, which contains secrets.

For an existing local conference environment that already uses `conference.yml`, `lab.yml`, and `protocols.yml`:

```sh
# Build all reference application, client, and optional lab images. This starts no lab services.
node scripts/mervikon.mjs build --lab
# Refresh applications and the already-selected conference/protocol services only.
node scripts/mervikon.mjs compose --conference --lab --protocols -- \
  --profile demo --profile conference --profile protocols up -d \
  --no-build --no-deps --force-recreate \
  workplace-bff supplier-bff finance-bff procurement-api fulfilment-api treasury-api \
  demo-console protocol-api conference-spa conference-bff conference-api conference-wrong conference-attacker
node scripts/mervikon.mjs preflight
node scripts/smoke.mjs
node scripts/conference.mjs preflight
```

Append your selected `--env-file` before the Compose `--` separator when using a file other than `.local/local.env`. The example requires those optional profiles to have been configured already; use only the overlays selected for your environment. Preserve `--conference` when recreating Workplace and Procurement to retain their demonstration controls. Worker and terminal images are rebuilt but remain one-shot clients; start them at the steps in [the walkthrough](walkthrough-local.md).

Recreating the three business APIs clears their **in-memory purchases, orders, and payment instructions**. Restart before creating the new rehearsal purchase. Demo Console observations also clear on restart; export any recordings you want to retain first. The isolated conference browser BFF uses in-memory tickets, so it may need a fresh sign-in afterward.

Keep `.local/`, the environment file, memberships, encryption certificates, sender keys, reference Redis and its persistent volume, and the external ProAuth installation intact. Identity-provider accounts, changed passwords, and registered passkeys live in ProAuth and must not be recreated or reset. Normal application BFFs reuse their existing persistent Redis stores and keys. Use the existing keys and provisioning records for this refresh.

Health and anonymous-access checks do not establish that every actor can sign in or satisfy step-up. Complete the [account preparation](walkthrough-local.md#prepare-priya-for-payment-release) and then rehearse the authenticated workflow.

## Checks

```sh
dotnet restore Mervikon.slnx --locked-mode --configfile .local/nuget.customer.config
dotnet build Mervikon.slnx --no-restore
dotnet test Mervikon.slnx --no-build
npm run check
node scripts/mervikon.mjs preflight
node scripts/smoke.mjs
```

Offline checks establish code and configuration consistency. Live login, protocol-negative cases, readback and rerun evidence are required before a scenario is labelled verified. Record sanitized outcomes separately from private CLI/browser artifacts.

## Actual upstream federation

The local business walkthrough uses native ProAuth UserStores. See [federation availability](federation.md) for the scope of upstream federation and SCIM synchronization.

## Additional local applications and workflow

For an environment initialized before these services existed, run `configure-workflows`, `generate-keys`, `provision`, `build`, then `compose -- up -d`. Existing values and enrolled credentials are preserved. All commands accept `--env-file`.

Use the [business walkthrough](walkthrough-local.md) for the implemented cross-service journey: purchasing, supplier handoff, workers, device authorization, Finance, and the optional replay comparison.

| Surface | Default address / invocation | Responsibility |
| --- | --- | --- |
| Workplace | `https://workplace.localtest.me` | Elena requests; Amir approves and hands approved orders to the supplier |
| Supplier Portal | `https://supplier.localtest.me` | Sam sees only assigned supplier orders and accepts them |
| Finance Desk | `https://finance.localtest.me` | Priya imports received orders and inspects internal payment instructions |
| Procurement | `https://procurement.localtest.me` | Owning business policy and actual introductory token exchange |
| Fulfilment | `https://fulfilment.localtest.me` | Supplier assignment, acceptance, dispatch and location-bound receipt |
| Treasury | `https://treasury.localtest.me` | Receipt synchronization, finance policy and reconciliation |
| Demo Console | `https://democonsole.localtest.me` (profile `demo`) | Local operator evidence viewer; no management credentials |

The default Compose model runs three BFF/React applications, three APIs and private Redis. `workers`, `devices`, `demo` and `unsafe-lab` are explicit additional profiles. A one-shot worker/terminal is a real executable component even though it is not continuously running in `docker compose ps`.

The standard setup assigns Sam to Northstar, Lea to the Zurich warehouse and Priya to Manufacturing finance. It also seeds the three [workload assignments](../config/business/workload-assignments.example.json). To inspect the current validated identity or run a one-shot client:

```sh
node scripts/mervikon.mjs compose -- run --rm -e WorkflowTask=identity dispatch-worker
node scripts/mervikon.mjs compose -- run --rm -e Operation=identity warehouse-terminal
# Membership changes take effect on the next decision; no API restart is needed.
node scripts/mervikon.mjs compose -- run --rm dispatch-worker
node scripts/mervikon.mjs compose -- run --rm -e Operation=receive \
  -e OrderId=ACTUAL_PURCHASE_GUID -e Revision=3 warehouse-terminal
node scripts/mervikon.mjs compose -- run --rm reconciliation-worker
```

The warehouse terminal prints ProAuth’s complete verification link when supplied, followed by the manual-entry address and user code. Complete login/consent in your own browser. Do not enter a password in the terminal. If a pending device authorization fails in an existing browser session, repeat with a clean dedicated browser session and inspect the actual result.

Finance uses the actual FAPI client profile, `private_key_jwt` with RSA/PS256, required PAR, PKCE and a separate DPoP key. The positive client flow has been exercised; this is not a full FAPI conformance claim. Payment release and approval above CHF 20,000 require trusted qualifying authentication within five minutes. A baseline token receives an RFC 9470 challenge; the BFF offers deliberate verification, followed by explicit business confirmation. The current Workplace → Procurement → Fulfilment handoff uses DPoP-bound tokens and policy-controlled exchange with Procurement’s own sender key. See [API assurance](api-assurance.md) and [the exchange contract](architecture.md#exchange-security-contract).

## Local Demo Console and isolated comparison

```sh
node scripts/mervikon.mjs compose -- --profile demo up -d
# Optional unsafe laboratory: import and build only when intentionally selected.
node scripts/mervikon.mjs configure-lab
node scripts/mervikon.mjs provision --lab
node scripts/mervikon.mjs build --service lab-api
node scripts/mervikon.mjs build --service lab-capture
node scripts/mervikon.mjs build --service lab-runner
node scripts/mervikon.mjs build --service lab-victim
node scripts/mervikon.mjs lab-run --lab
```

Open the HTTPS viewer at `https://democonsole.localtest.me`. The read-only viewer opens without a login. The loopback port remains available for local tooling. The private evidence network carries authenticated ingestion on 8081. Treat local processes and containers on that network as trusted.

Business actions produce bounded, allowlisted HTTP observations, trace/span IDs and resulting state where observed. Previous state, actor identity, individual validators and missing BFF/AS spans are explicitly unavailable. Three views, keyboard stepping, presenter mode and JSON export operate on observations after requests complete. The observer is optional and cannot grant access; offline delivery produces incomplete recordings. Retention is 20 traces / 24 hours and clears on restart.

**Intentionally unsafe demonstration:** the lab TLS terminator captures the actual authorization header and proof, and an isolated runner attempts replay without victim keys. Both targets retain real package validation. Captures live only in bounded process memory. Only the configured issuer is reachable through the victim/API's TLS egress proxy; the runner cannot use it. No lab ports are published. The issuer route currently assumes the local shared stack; hosted issuer/network variants need independent validation.

`lab-run` serializes both variants, verifies token-expiry rejection and capture deletion, exports only validated outcome fields, then stops lab services. The Console's comparison is always labelled **Recorded evidence**. Raw logs remain private. If cleanup fails, the command fails and stops the lab; inspect its private diagnostics before removing a stale `.local/lab-run.lock`. Never overlap direct manual lab runs. The 30-second lifetime is specific to the isolated lab tenant.

The lab's fixed subnet is `172.31.252.0/24`; change its subnet and exact `CaptureProxyIp` together if that conflicts with another local network. `lab-run` is a single-process comparison. The separate [replica control](replica-controls.md) verifies two stateless API instances with an atomic Redis store, outage and restart. Both are single-operator labs; hosted visitor isolation remains unverified.

The [protocol controls](protocol-controls.md) add an opt-in dedicated tenant and API for JWT, opaque and encrypted tokens, signed introspection and mTLS. Run `configure-workflows` when upgrading to add missing settings and the Console's sanitized comparison file without rotating existing credentials. Choose **Protocol controls** in the Console to view these recorded outcomes.

## BFF replica checks

Use the [BFF replica walkthrough](bff-replicas.md) for the shared-proxy overlay, real concurrent refresh, failover and server-side logout checks. The optional overlay scales Workplace only and preserves the single-instance business services.

## Identity Protocols conference route

Use [the conference runbook](conference.md) for the four slide demo slots, the real browser theft comparison, optional nonce/FAPI probes and offline evidence fallback.

## Migrating an earlier deployment

Application identifiers now use Mervikon consistently. The solution is `Mervikon.slnx`, the operator entry point is `scripts/mervikon.mjs` (also `npm run mervikon -- ...`), and the Helm chart is `deploy/helm/mervikon`. The repository title and ProAuth package names are unchanged.

This is a coordinated deployment change: audiences are `urn:mervikon:*`; company environment variables use `MERVIKON_*`; Compose/image/router names, Redis prefixes, BFF cookies, data-protection purposes and lab headers also changed. Existing sessions and tokens using the previous names cannot be reused. Resource ownership GUIDs in `config/proauth/ownership.json` are unchanged.

For a clean rehearsal deployment, stop the previous application stack using its previous checkout and private environment before starting a new stack on the same hostnames. Generate a new private environment with `node scripts/mervikon.mjs init-customer --env-file .local/mervikon/local.env`, then follow the connection instructions with that environment file. Configure the dedicated `mervikon_manufacturing`, `mervikon_research` and `mervikon_partners` UserStore databases from the environment template. Rebuild images, regenerate environment-specific keys, provision the matching manifests, publish current business memberships, and sign in again. Optional protocol/conference environments must likewise use their matching renamed issuer path, key IDs, proxy routes and headers.

Keep the old private environment and keys until the new rehearsal is verified. The tooling does not migrate UserStore data or reset the external ProAuth installation. If you need to retain existing accounts or passkey enrollments, arrange a database migration separately before switching the UserStore connections. Do not regenerate ownership GUIDs or reuse an old token as a test of the new audience contract.
