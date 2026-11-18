# Try the business journey

Follow one purchase through the implemented applications, using real ProAuth authentication and authorization decisions. The fictional Mervikon Group purchases an Industrial Laser Calibration Station from Northstar Components; Alpine Logistics receives it in Zurich. No real bank or payment provider is connected.

## Before you start

Connect the reference to your **ProAuth test installation** and follow [running instructions](running.md) to provision its dedicated clients, policies, and fictional accounts. Provisioning assigns the walkthrough accounts to their business roles. Complete each new account’s first-use password change. Enroll a qualifying authentication method for Priya before payment release, and for Amir before trying high-value approval.

Use separate browser profiles for each actor so their sessions remain independent. In Chrome, open the profile menu at the top right, select **Add Chrome profile**, continue without a Google account, and name the profile for the actor. Open Workplace in Amir’s new window to approve Elena’s existing request. You can switch windows without signing either actor out. [Chrome profile instructions](https://support.google.com/chrome/answer/2364824?hl=en)

Commands below use `.local/local.env` by default. If you chose another file, add `--env-file .local/mervikon.env` to each command, before the `--` separator in Compose commands. The walkthrough uses the default local addresses below; substitute your configured application addresses if you changed them.

| Actor | Login name | Provider to select | Application | Role in the journey |
| --- | --- | --- | --- | --- |
| Elena | `elena` | **Mervikon Manufacturing · local training accounts** | Workplace | Requests equipment |
| Amir | `amir` | **Mervikon Manufacturing · local training accounts** | Workplace | Approves purchases and hands them to the supplier |
| Sam | `sam` | **Partner · local training accounts** | Supplier Portal | Accepts the supplier's assigned orders |
| Lea | `lea` | **Partner · local training accounts** | Warehouse Terminal and a browser | Confirms delivery after device authorization |
| Priya | `priya` | **Mervikon Manufacturing · local training accounts** | Finance Desk | Imports receipts and releases payment instructions |

### Passwords and first-use setup

There is no shared password published with this repository. On a newly provisioned installation, the operator supplies `INITIAL_USER_PASSWORD` in the private environment file. Each new account must change that password at first sign-in. On an existing installation, use the account's current password; the initial provisioning value is not a password reset. Repeat provisioning must not be used to recover or reset rehearsal credentials.

Record changed passwords in your password manager.

If login fails, check the selected provider and the current password before attempting recovery through your ProAuth operator. Do not recreate users: their subjects, memberships, and passkeys belong to the existing identities.

### Prepare Priya for payment release

Before creating the purchase, sign in to **Finance Desk** as `priya` through **Manufacturing**, and choose **Manage enrolled authentication methods**. This opens ProAuth's account-security page for that account. Confirm the account is Priya, then verify that she has a usable enrolled passkey or other configured method meeting the assurance policy. If needed, add a passkey in **Login Passkeys** using the authenticator you will use for rehearsal. Preserve existing passwords, passkeys, and recovery methods.

Follow [account selection and enrollment](account-security.md#open-the-correct-account). Enrollment under Nina's Research account or Amir's account does not enroll a credential for Priya. Merely enrolling a method does not upgrade an existing API token: the payment step will request fresh verification when needed. If you cannot complete qualifying authentication, expect the instruction to remain **Pending**.

Amir needs the same preparation through **Workplace → Account security**, under his Manufacturing account, only when trying approvals above CHF 20,000. The CHF 18,500 purchase below does not require approval step-up.

The standard journey uses native ProAuth accounts, including Sam's partner account. [Upstream federation and SCIM](federation.md) are not required or enabled. Membership changes are read on each business decision; restarting an API clears its in-memory business records.

```sh
node scripts/mervikon.mjs check
# Fresh standard environment; for an existing conference environment, preserve its overlays:
# see running.md#refresh-an-existing-rehearsal-environment
node scripts/mervikon.mjs compose -- --profile demo up -d
```

If this environment was already running, first follow [the refresh procedure](running.md#refresh-an-existing-rehearsal-environment) when changing application versions. Keep the existing overlay selection; omitting the conference overlay when recreating Workplace or Procurement removes their conference controls.

Open [Demo Console](https://democonsole.localtest.me) alongside Workplace. The read-only viewer opens without a login and displays observations across the actors in your run.

## Purchase to receipt

1. In `https://workplace.localtest.me`, sign in as Elena through Manufacturing. Request the CHF 18,500 calibration station. The request should be **Requested**, revision 1. The API, rather than the React role display, owns authorization.
2. In Amir's separate Workplace profile, approve that request. The request should become **Approved**, revision 2. Choose **Hand to supplier** to send it to Fulfilment. Procurement performs an actual RFC 8693 exchange for the narrower downstream audience. Workplace’s token is DPoP-bound. Procurement validates the original request, exchanges under an explicit ProAuth policy and uses its own DPoP key for Fulfilment. This is the current SCN-13 route; see the [exchange contract](architecture.md#exchange-security-contract).
3. In `https://supplier.localtest.me`, choose the native partner account provider and authenticate as the local Sam fixture. Sam sees the assigned Northstar order and accepts it, producing Accepted, revision 2. The explicitly mapped business identity grants assignment access; email matching does not.
4. Run the dispatch workload:

   ```sh
   node scripts/mervikon.mjs compose -- run --rm dispatch-worker
   ```

   With its [explicit workload pair](authorization.md#workload-decisions), the worker obtains its own client-credentials token and moves the accepted order to Dispatched, revision 3. It does not impersonate Sam or gain employee approval authority. Copy the actual purchase GUID printed by the worker.
5. Start the warehouse operation using that GUID:

   ```sh
   node scripts/mervikon.mjs compose -- run --rm \
     -e Operation=receive -e OrderId=ACTUAL_PURCHASE_GUID -e Revision=3 warehouse-terminal
   ```

   Open the complete verification link in Lea’s browser profile. The link carries the user code. For manual entry, open the shorter verification address and type the code shown on the terminal. Select the local Partner provider, authenticate as Lea, and authorize the listed scopes/resource. No password is entered in the terminal. The device polls ProAuth and sends its token to Fulfilment; success produces Received, revision 4. The API enforces Lea's warehouse role and Zurich location.

Use the Console's Business and Protocol views to inspect actual HTTP observations, resulting states where collected, and correlated API spans. A successful HTTP result does not identify every internal validator or establish protocol certification.

## Receipt to internal payment instruction

1. In `https://finance.localtest.me`, authenticate Priya through Manufacturing. This registration uses the real FAPI client profile with required PAR, PKCE, RSA/PS256 private-key assertions and a separate DPoP key.
2. Select **Import received orders**. Treasury independently authenticates its reader workload to Fulfilment, imports the matching receipt, and creates one Pending CHF 18,500 instruction. Repeating import must not create another instruction.
3. Select **Release**. Baseline authentication receives the RFC 9470 challenge and the instruction stays Pending. Follow [the assurance route](api-assurance.md): verify with Priya’s enrolled qualifying factor, return, review the current recipient/amount/revision, and explicitly Release again. Sender possession and login alone do not satisfy this requirement; the internal release is not a FAPI certification claim.
4. Run the independent read-only workload:

   ```sh
   node scripts/mervikon.mjs compose -- run --rm reconciliation-worker
   ```

   It reports the actual instruction count without release authority. If a grant is revoked or unusable, fresh authorization is recovery, not evidence of successful renewal.

## Captured bearer versus DPoP

Build and provision the opt-in lab as described in [running instructions](running.md#local-demo-console-and-isolated-comparison). Then run:

```sh
node scripts/mervikon.mjs lab-run --lab
```

The lab TLS terminator is intentionally unsafe and captures actual authorization artifacts. It is separate from the normal applications. The attacker runner receives no victim key or OAuth client credential. The legitimate request succeeds before and after the attack; the stolen bearer works, whereas a token-only DPoP request and replay of the captured proof fail. Cleanup verifies expired-token rejection and capture deletion before stopping the lab.

In the Console choose **Bearer / DPoP comparison**. These are recorded HTTP outcomes with observation timestamps, never a live readiness claim. Single-process replay evidence does not establish cross-replica behavior, and the exact rejecting validator is not instrumented. Export contains allowlisted outcomes only; raw capture material must not be shared.

## Scope and reset

Continue with [native account security](account-security.md), [token formats and mTLS](protocol-controls.md), [cross-replica replay/outage](replica-controls.md), and [BFF sessions](bff-replicas.md). The Console shows recorded experiments, not a readiness guarantee. [Federation](federation.md) and [SCIM lifecycle](lifecycle.md) are unavailable in the runnable profile.

Export Console observations before restarting it. Restart only owned reference business APIs to clear their in-memory records; this is a destructive reset for those demonstration records. Preserve ProAuth identities, Redis security state and keys. Do not delete the external product's SQL container to reset a purchase.
