# Who may approve a Mervikon purchase?

A valid Procurement token lets the API identify a caller. Purchasing still needs to know **which person** is acting, **which buyer and cost centre** that person may represent, and **whether this transaction** is eligible. For example, Elena may request a CHF 18,500 station; even with both roles, neither Elena nor her second verified login may approve it. Amir can approve it for Manufacturing/engineering within his CHF 50,000 limit. Another buyer or cost centre sees 404, an insufficient limit gets 403, and a second approval of the same revision gets 409.

| Direct assignment (default) | Native group-governed approval (opt-in) |
|---|---|
| Resource server validates the API access token: signature, issuer, audience, type, lifetime and sender constraint where applicable. Route requires its scope. | Same validation. Group claims, display names, headers, email and the BFF’s ID token have no part in this decision. |
| Exactly one issuer, subject and trusted issuer tenant select a local assignment. No row: 403. The read-scoped identity diagnostic works before enrollment. | Same local row required. Group without assignment: 403. |
| The row gives the stable person ID, roles, buyer, cost centre and CHF limit. Verified aliases act as the same person. | The approver role additionally requires a fresh, separately provisioned eligibility fact for the same issuer, tenant and person. No match removes **only** the approver role. |
| Request requires requester role. Approval requires approver role, matching partition, another person as requester, sufficient limit, Requested state and expected revision. | Identical. Neither group management nor the provisioner can set a limit, assign a buyer or approve a transaction. |
| Over CHF 20,000: challenge unless original authentication satisfies the [assurance policy](api-assurance.md). On explicit confirmation the endpoint reads the assignment and the book repeats the transaction checks under its write lock. | Same checks and re-read, including eligibility; no automatic post-verification write. |
| Remove the row or role to withdraw access with the current token. | Local withdrawal is independent of the group and takes effect on the next visible read. For directory removal and stale tokens see [the provisioning profile](group-approvals.md). |

## Assignments and updates

The purchasing administrator verifies the account using the read-scoped `/api/identity` and an independent personnel/account record. Assign an immutable, never-recycled business-person ID from that record. Choose the authorized buyer/cost centre, exact roles and CHF ceiling from purchasing policy; do not derive them from tenant membership. Use `SubjectAliases` on the same row for independently verified alternate subjects under the same issuer and tenant. For a second trusted tenant/issuer, enroll the verified tuple explicitly with the **same** person ID. The application cannot discover an undeclared identity link; never mint a new employee ID just because a subject changes. Multiple subjects with the same email prove nothing.

See [demo setup](running.md#business-membership-and-the-first-journey). For manual enrollment, a membership row contains `Issuer`, `Subject`, `TenantId`, and `Member` as illustrated by the [business fixtures](../config/business/demo-members.json). Place rows under `Memberships` in `memberships.json`. The APIs validate the entire business document before listening and on every decision. A file over 256 KiB, missing required field, unknown property (including trust settings), duplicate property, duplicate identity/alias or duplicate workload key/pair is invalid. A bad startup document prevents startup. A bad live document returns 503; no prior grant is cached. Scope and route-specific client checks still apply. A bad document cannot change discovery, audience, proxy settings or the path of the next read.

Use a single authorized writer and a read-only **directory** bind. Stage a complete file in that same directory, set its reader permissions, then rename it over the old name:

```sh
# ENTITLEMENT_DIR contains only business assignments, under a private host parent.
install -m 0644 /private/staging/reviewed.json "$ENTITLEMENT_DIR/memberships.next"
mv -f "$ENTITLEMENT_DIR/memberships.next" "$ENTITLEMENT_DIR/memberships.json"
```

Do not edit the live file in place or bind only its inode. An open request may finish using the snapshot it already read. The second approval check narrows that interval; a file update is not a distributed transaction with the business write. Subsequent opens see the replacement once it reaches that mount. A transient read error denies access. For multiple replicas, distribute the same atomic version to **every** replica and verify denials on each. There is no watcher, cluster-wide invalidation or proven cross-host time bound. Do not scale the in-memory business stores to demonstrate authorization updates.

## Workload decisions

Use the [three explicit assignment rows](../config/business/workload-assignments.example.json). The API validates the workload token, scope and expected client, then looks up **issuer + trusted tenant + client** in the workload table. A scope, matching tenant, human membership, or an unmapped registration is insufficient. Each row lists exact ordered pairs. Granting (Manufacturing, engineering) and (Research, lab) never grants (Research, engineering). There is no wildcard or default.

| Data owner / operation | Response and denial boundary |
|---|---|
| Fulfilment dispatch queue | Only Accepted orders for granted pairs; purchase ID and revision. POST checks the same pair, current revision and transition before dispatching. An unassigned ID gets 404. Supplier and warehouse interactive routes use their registered clients and member policies. |
| Fulfilment `GET /api/received` | Only Received orders in a granted pair. Fields: purchase ID, buyer, cost centre, supplier, CHF amount, source revision. No requester, approver, product or warehouse. Omitting both query parameters returns all **explicitly granted** pairs. With a query both parts must name one allowed pair; otherwise 403 upstream. |
| Treasury `POST /api/payments/synchronize` | Finance Desk, write scope and finance member required **before** obtaining a workload token. Treasury sends exactly the member’s pair to Fulfilment. An upstream 403 produces a sanitized 403 (`receipt_read_not_authorized`) and no import. Treasury rechecks the member after I/O and rejects the entire response if any receipt lies outside that pair. Import is idempotent by purchase ID and receipt contents. |
| Treasury `GET /api/reconciliation` | Separately assigned reconciliation client gets only purchase ID, CHF amount and payment status in its own pairs. No recipient, releasing person or release parameters. Payment writes independently require Finance Desk, its write scope and current member authority; release also requires transaction checks and fresh assurance. |

## Handoff and negative control

Procurement obtains the original token through the product’s validated-token accessor. The exchange must return a nonempty token with type DPoP, case-insensitive; a bearer response returns 502 **before any call to Fulfilment**. The product proof handler signs the downstream request with Procurement’s key. The Fulfilment resource server checks audience and key binding; the import endpoint checks actor, subject membership, buyer, cost centre, limit, approver/requester separation and approved order details. The registered receipt-reader path still deliberately uses bearer. The optional escalation probe counts only HTTP 400 / `invalid_scope` as a passed denial. Unexpected success, wrong credentials and transport/timeout errors stop the handoff.

## Source listings and reproduction

| Decision | Copyable source and coverage |
|---|---|
| Identity → member → purchase | [API registration](../src/shared/Common.Security/ApiSecurity.cs), [singleton claims](../src/shared/Common.Security/ValidatedContext.cs), [bounded JSON reader](../src/shared/Common.Security/AssignmentJson.cs), [assignments](../src/shared/Common.Security/Memberships.cs), [request and approval endpoints](../src/services/Procurement.Api/ProcurementEndpoints.cs), [locked transaction rules](../src/services/Procurement.Domain/PurchaseBook.cs) |
| Dispatch and receipts | [Fulfilment HTTP handlers](../src/services/Fulfilment.Api/FulfilmentEndpoints.cs), [order store](../src/services/Workflow.Domain/OrderBook.cs), [wire projections](../src/services/Workflow.Domain/WorkloadViews.cs) |
| Finance and reconciliation | [Treasury HTTP handlers](../src/services/Treasury.Api/TreasuryEndpoints.cs), [payment store](../src/services/Workflow.Domain/PaymentBook.cs), [worker](../src/clients/Integration.Worker/Program.cs) |
| Constrained handoff | [exchange and denial probe](../src/services/Procurement.Api/ProcurementEndpoints.cs), [proof handler registration](../src/services/Procurement.Api/Program.cs), [actor and binding checks](../src/shared/Common.Security/ExchangeActor.cs) |
| HTTP checks | [purchase and handoff](../tests/Authorization.Http.Tests/PurchaseHttpTests.cs), [workload boundaries](../tests/Authorization.Http.Tests/WorkloadHttpTests.cs), [group intersection](../tests/Authorization.Http.Tests/GroupApprovalTests.cs). Kestrel serves the actual routes; signed fixture JWTs and static signing keys replace issuer discovery, and test token responses replace the AS. The DPoP success uses the product’s proof handler and validator. |
| Real issuer and Docker mount | [seeded host](../tests/integration/MountedFulfilment.cs) and [driver](../tests/integration/mounted-assignments.mjs). One existing dispatch credential, TLS, one temporary container and an entitlement-only mount. No shared account, grant or business record changes. |

After the [standard locked build](development.md), run `dotnet test tests/Authorization.Http.Tests -c Release --no-restore`. For the separate local shared-proxy integration check, use the matching trusted reference Fulfilment runtime image and the configured dispatch client. The fixture’s source is mounted separately from grants. The driver generates a temporary TLS keypair (OpenSSL with `-addext` required), obtains one issuer token and prints statuses and timing only:

```sh
out=$(mktemp -d)
dotnet publish tests/integration/MountedFulfilment.cs -c Release -o "$out" \
  -p:PublishAot=false -p:RestoreLockedMode=true -p:RestoreConfigFile="$PWD/.local/nuget.customer.config"
node tests/integration/mounted-assignments.mjs /private/path/mervikon.env "$out"
```

On the local shared issuer, the pre-enrollment diagnostic returned 200; the same token saw membership 403 → 200 → 403, malformed trust-key injection 503, and dispatch of the *original* accepted order 200 after restoration. The first 403 after removal was observed within 19–21 ms in two runs; that is a measurement, not a propagation guarantee. No accounts were reset. Durable financial records, audit/outbox delivery, upstream federation/SCIM and cloud deployment remain separate work.
