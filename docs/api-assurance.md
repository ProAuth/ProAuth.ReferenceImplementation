# API assurance and deliberate step-up

The APIs use the ProAuth assurance contract. The exact package release is recorded in `config/product-build.json`; see [dependency setup](development.md#proauth-dependency).

## What changes in the business journey

- Approval up to CHF 20,000 keeps the ordinary business policy.
- Approval above CHF 20,000 and every payment release require canonical `https://proauth.net/acr/mfa` or `phr`, with original `auth_time` no older than 300 seconds. The API uses the product's assurance helper after signature/introspection, audience, lifetime, sender binding and business authorization.
- Missing, weak, malformed, duplicate, future or stale evidence fails closed. A newer `iat`, successful FAPI login, possession of a DPoP key, an ID token or a browser assertion cannot substitute for this evidence.
- The API responds with HTTP 401 and the product RFC 9470 challenge. The React app offers an explicit verification link. It does not automatically navigate, retry the write or retain its body for replay.
- `/step-up` prepares an authenticated, no-store form with ASP.NET Core antiforgery. Its POST enters the package-owned `/bff/account/step-up` using a fixed named policy and local continuation. The BFF requires fresh authentication (`max_age=0`), preserves the subject/issuer, and owns protected state, correlation, nonce, PKCE and PAR.
- After return, current records are loaded. The user must approve or release again. The API rechecks membership, organization, role, amount/limit, separation of duties and current revision. Payment recipient and amount must still match. A cancellation, unmet factor or changed subject cannot perform a business write or start an automatic retry.

The accepting contexts are explicit. No numeric ACR ranking, alias inference or hardware-protection claim is made. `amr` disclosure is disabled because these business requirements do not need it.

## Configuration and rollout

The existing ClientApp YAML resources contain stable, owned `authenticationAssurancePolicies`:

| Client | Tenant | Exact audience | Mode |
| --- | --- | --- | --- |
| Workplace | Workforce | `urn:mervikon:procurement` | StepUp |
| Finance Desk | Workforce | `urn:mervikon:treasury` | StepUp |
| Procurement exchange | Workforce | `urn:mervikon:fulfilment` | EvidenceOnly |

The exchange destination policy permits disclosure of preserved original evidence; it does not independently authorize an exchange, refresh the user's authentication or grant supplier rights. Existing DPoP policy and business checks remain in force. Machine-only grants cannot manufacture user assurance.

The standard `npm start` setup provisions these policies and starts Workplace, Finance Desk, and their APIs. Follow the [connection instructions](running.md#connect-your-proauth-installation). If you already have a configured environment with the policies enabled, continue with the walkthrough. After enabling the policies on an existing installation, sign in again to obtain a token with the new claims.

## Approval and payment walkthrough

Before starting, confirm a usable user-verified passkey or qualifying configured MFA method for **Amir in Manufacturing** through Workplace → **Account security**, and **Priya in Manufacturing** through Finance Desk → **Manage enrolled authentication methods**. Reuse existing enrolled methods where possible. Follow [the actor-specific preparation](walkthrough-local.md#prepare-priya-for-payment-release); enrolling under Research Nina does not prepare either Manufacturing account. Keep recovery credentials and other users intact. Enabling an assurance policy does not enroll a factor. A password-only account must not be presented as MFA. Automated tests may use clearly labelled virtual authenticators against real WebAuthn endpoints; that is test infrastructure, not proof of physical hardware protection.

1. Sign in with passwords. Elena requests the CHF 24,900 coffee infrastructure; Amir attempts approval. Show the API challenge and the unchanged Requested record.
2. Choose **Verify before approving**, then **Verify identity**. Authenticate as the same Amir using the enrolled passkey (or an actually configured qualifying MFA method).
3. On return, show that the record is still Requested. Review it and click **Approve** again. Then continue the existing supplier/dispatch/warehouse journey.
4. Priya imports the receipt and attempts Release with baseline authentication. Show the challenge and unchanged Pending instruction.
5. Verify with Priya's enrolled factor, review the amount/recipient/revision, and explicitly Release again. This remains an internal demonstration instruction: no bank is contacted.
6. Let more than five minutes elapse after stronger authentication. Another sensitive action must require fresh verification even if the access token has refreshed. Do not edit token timestamps to demonstrate freshness.

For rejection controls, try a requester approving their own purchase, a changed revision or recipient, a password-only verification attempt, cancellation and a missing-antiforgery POST. Stronger authentication must never repair missing business permission.
