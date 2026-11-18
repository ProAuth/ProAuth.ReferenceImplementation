# Native account security walkthrough

This implements the local registration and sign-in subset of SCN-07. Use the native account belonging to the person and provider you intend to verify. Credentials are registered per account; enrollment for one actor does not prepare another. The product owns account management, credential storage and WebAuthn ceremonies. The reference does not implement a substitute authenticator or recovery service.

## Open the correct account

Use a dedicated browser profile for the actor and select the appropriate provider:

| Purpose | Login and provider | Where to open account security |
| --- | --- | --- |
| Payment release | `priya` · **Mervikon Manufacturing · local training accounts** | Finance Desk → **Manage enrolled authentication methods** |
| High-value purchase approval | `amir` · **Mervikon Manufacturing · local training accounts** | Workplace → **Account security** |
| Standalone Research account demonstration | `nina` · **Mervikon Research · local training accounts** | Workplace → **Account security** |

Confirm the displayed account before enrolling. Existing passkeys and passwords remain valid across reference application rebuilds; do not remove or replace them as an ordinary reset. If a suitable method is already enrolled and usable with your browser/authenticator, reuse it.

The BFF accepts only the authenticated session's `pa_account_management_url`, checks the exact HTTPS issuer origin, `/account/security` path, configured tenant and a single provider identifier, and redirects. Missing or ambiguous context returns HTTP 409. Caller-supplied return URLs and arbitrary external destinations are not accepted. Sign in afresh as the intended account if you have a stale session; a newly provisioned environment also needs its navigation claim rule imported.

The owned claim rule in `config/proauth/policies/31-account-security.yml` selects only this navigation claim into the ID token. It runs at the same priority as the independent API-context selection: ProAuth processes priority groups sequentially, so a later filtering group would lose original pipeline input and filter away earlier output. The navigation claim is not API authorization or assurance evidence. Renew an existing application authorization after importing the rule.

## Enroll and sign in

1. In **Login Passkeys**, add a clearly named disposable credential. For a presentation, use a real authenticator under the configured RP origin. Keep the password and recovery configuration intact.
2. Sign out of both the application and ProAuth. Start a new login in the appropriate application through the same provider and select **Sign in with a passkey**.
3. Complete the real assertion and consent. Verify application authentication and the account portal's last-used metadata. Business access still requires an independent application membership.
4. Only for an optional credential-removal experiment, attempt removal through the account portal and observe its actual policy decision. If the final credential is protected, expect a refusal; do not claim deletion or silently weaken policy. A revoked-credential comparison requires a permitted replacement and a confirmed successful removal first.

## Verification limits

Verify the actual account policy when exercising enrollment or removal. A rejected removal is not evidence that a revoked credential was accepted. Closing a virtual authenticator does not delete its server registration. Virtual authenticators exercise real WebAuthn endpoints but do not establish physical hardware or attestation properties. Recovery and wrong-origin tests require their own explicit controls.

## Safe evidence and reset

Record operation, timestamp, HTTP status and policy code only. Do not export credential private keys, recovery codes, cookies, OIDC responses or password-reset links to the Demo Console. Virtual authenticators are test infrastructure; label their use and keep their state private. Remove only the named disposable credential when product policy permits, then remove the virtual device. Registration and browser-device cleanup are separate steps.

Enrollment alone does not authorize a business action. The [API assurance integration](api-assurance.md) now verifies earned strength and original authentication freshness on each sensitive API operation.
