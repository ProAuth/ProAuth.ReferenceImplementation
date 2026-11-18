# Architecture

The reference application connects to **your ProAuth installation**, which runs separately and provides authentication, token issuance, and identity policies. Use a test deployment with Enterprise features and customer package-feed access. See the [connection and evaluation instructions](running.md#connect-your-proauth-installation).

The browser applications and BFFs, three business APIs, workload/device clients, and evidence viewer are implemented here. The standard workflow uses native ProAuth accounts. Upstream federation and SCIM synchronization are not part of the runnable journey.

## Business journey

Workplace submits equipment requests to Procurement. A different authorized person approves a request. Procurement exchanges its validated incoming access token for a Fulfilment token, using its own sender key and a constrained exchange policy. Fulfilment assigns the order to the supplier, then accepts dispatch and receipt transitions. Treasury reads received orders with a workload credential, creates internal payment instructions, and requires fresh strong user authentication to release them.

| Component | Responsibility |
| --- | --- |
| Workplace BFF / Procurement API | Browser session, purchase requests, separation of duties, approval limits, token exchange |
| Supplier Portal BFF / Fulfilment API | Assigned supplier orders, dispatch and warehouse receipt |
| Finance Desk BFF / Treasury API | Received-order synchronization, payment confirmation and release |
| Integration.Worker | Workload-authenticated supplier dispatch and read-only payment reconciliation |
| Warehouse.Terminal | Device authorization and warehouse operations |
| Demo Console | Bounded, sanitized observations for a local operator |

## Reading the integration code

The code assumes familiarity with .NET and basic OAuth, and explains the additional protocol boundaries at the call sites. For a feature from the whitepaper or a conference demonstration, follow this path:

1. **Host (`Program.cs`):** read the service overview, SDK registration and middleware order. This is where the service composes its protocol integration and shared infrastructure.
2. **Application configuration (`appsettings.json` and deployment settings):** inspect the audience, token presentation policy and outbound authentication. Secrets and environment-specific values are supplied by deployment.
3. **ProAuth registration (`config/proauth`):** inspect the other side of the contract: client type, allowed resource/scopes, sender binding, exchange or assurance policy. The adjacent comments explain why those settings exist.
4. **Named endpoint handler:** follow scope authorization into caller/actor checks and current business membership. An authenticated token is the beginning of the decision, not the decision itself.
5. **Domain method:** follow organization, role, amount, revision and state-transition checks. These belong to Mervikon; the OAuth library cannot infer them.

| Whitepaper / talk concept | Source entry point | Deliberate distinction |
| --- | --- | --- |
| Baseline and browser boundary | [Workplace startup](../src/apps/Workplace.Bff/Program.cs), [browser requests](../src/apps/shared/api.ts) | Browser cookie, server-side token, SDK proxy CSRF checks |
| Sender constraints and delegation | [Procurement startup](../src/services/Procurement.Api/Program.cs), `HandOffApprovedPurchase` in [its endpoints](../src/services/Procurement.Api/ProcurementEndpoints.cs) | Incoming Workplace DPoP token versus a new Fulfilment token bound to Procurement's key |
| Receiving delegated authority | `ImportApprovedOrder` in [Fulfilment endpoints](../src/services/Fulfilment.Api/FulfilmentEndpoints.cs), [ExchangeActor](../src/shared/Common.Security/ExchangeActor.cs) | Human subject, service actor, scope and business approval are independently checked |
| Workloads and devices | [Integration.Worker](../src/clients/Integration.Worker/Program.cs), [Warehouse.Terminal](../src/clients/Warehouse.Terminal/Program.cs) | Workload assignments versus a device-authorized person's warehouse membership |
| Sensitive actions | [Procurement endpoints](../src/services/Procurement.Api/ProcurementEndpoints.cs), [Treasury endpoints](../src/services/Treasury.Api/TreasuryEndpoints.cs), [StepUpEndpoints](../src/apps/shared/StepUpEndpoints.cs) | API challenge, fresh verification, then a new explicit business confirmation |
| High-assurance client profile | [Finance Desk startup](../src/apps/FinanceDesk.Bff/Program.cs) and [settings](../src/apps/FinanceDesk.Bff/appsettings.json) | PAR, client authentication, DPoP and user assurance serve different purposes |
| Native group eligibility | [Group.Provisioner](../src/clients/Group.Provisioner/Program.cs) | Operator credentials and provisioning stay outside the business API request path |

The services share validation conventions, but their security profiles differ:

| Service | Incoming access | Outbound access / additional enforcement |
| --- | --- | --- |
| Procurement | DPoP required, Procurement audience | DPoP token exchange to Fulfilment; fresh assurance for purchases above CHF 20,000 |
| Fulfilment | Bearer and sender-constrained, Fulfilment audience | Supplier and warehouse client restrictions; single Procurement actor for import; bounded workload partitions |
| Treasury | Bearer and sender-constrained, Treasury audience | Payment routes require DPoP; receipt reading uses Treasury's bearer workload; release requires fresh assurance |

Shared code is named for its responsibility (`AddScopePolicies`, `AddBusinessMemberships`, `ConfigureBffCookie`). Company-owned identifiers use Mervikon names, including `urn:mervikon:*` audiences. ProAuth package and protocol names remain unchanged. In particular, `ReferenceToken` in the protocol lab means an opaque token and is not company branding.

`AddDemoEvidence`, `UseDemoEvidence`, endpoint evidence metadata and conference controls are explicitly demonstration support. They help explain observed results and are separate from authorization. The optional unsafe labs remain outside the normal business flow.

## Trust boundaries

Browsers use same-origin BFF endpoints. OAuth tokens stay on the server. The BFF package handles authentication, proxy CSRF validation and refresh. Its session token store reads the current browser ticket. The ProAuth Redis ticket store protects each ticket with the BFF’s Data Protection keys and binds it to its application and session key. Each API independently validates the issuer, audience, token lifetime, required scopes, and configured sender binding through the ProAuth resource-server package.

`Program.cs` in each API explicitly composes resource-server authentication, scope policies and middleware. `Common.Security` supplies shared issuer-validation conventions, membership resolution and domain-error handling. Membership binds the exact verified issuer, subject, and tenant to an operator-owned business identity. Every business decision reads the membership file again; missing or malformed configuration fails closed. Authentication alone does not grant a business role.

Domain stores enforce organization and cost-centre boundaries, roles, allowed transitions, revision checks, and approval limits under a process-local lock. Receipt requires the assigned logistics organization and warehouse. Purchase approval forbids self-approval. Payment release must match the displayed amount, recipient, and revision. [API assurance](api-assurance.md) adds trusted authentication strength and freshness to sensitive operations.

## Exchange security contract

Procurement obtains the incoming token only through the package's validated-token accessor. The authorization server permits the configured exchange client to request the specific destination resource and scope. Fulfilment checks the expected exchange actor in addition to token validation and application membership. The original sender's proof is never forwarded to another resource. Procurement accepts only a DPoP exchange result (token type compared case-insensitively) before creating a downstream request. The proof handler receives the canonical DPoP scheme. Token exchange does not grant supplier permissions or renew authentication freshness. See the [decision trace and wire projections](authorization.md).

## State and deployment

The domain books are intentionally resettable teaching stores. Idempotency and concurrency guarantees apply within one process lifetime. Durable storage, cross-service transaction recovery, and multi-instance business APIs require a separate persistence design. See [deployment](deployment.md).

Redis-backed BFF tickets, tokens, locks, and Data Protection keys are security state. Use persistent storage and preserve encryption certificates. Trust only configured reverse proxies. TLS validation remains enabled.

Evidence is optional and cannot authorize a request. Its schemas exclude raw tokens, proofs, credentials, and arbitrary exception details. The local viewer labels observations as recorded; unavailable observations remain unavailable.
