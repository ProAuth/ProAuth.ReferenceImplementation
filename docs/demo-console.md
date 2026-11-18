# Demo Console

Demo Console displays recorded API observations, protocol projections, and negative-control results. It is optional: normal applications do not depend on it to authenticate or authorize operations.

Open `https://democonsole.localtest.me` with the `demo` profile enabled. Anyone who can reach the viewer can read the demo observations. Use the configured `DEMO_CONSOLE_HOST` through Traefik or the local loopback port. The separate ingestion listener accepts authenticated publisher POSTs with a bounded body size. It must remain private. Observations use closed schemas and exclude raw credentials and token values.

Business evidence is held in process memory, bounded to 20 traces and 1,000 observations, and expires after 24 hours. Protocol evidence is bounded to 500 observations. Restarting the Console clears both. Offline publication makes recordings incomplete; it does not change business authorization.

The [scenario index](scenarios/registry.md) supplies lesson metadata. A lesson label is not evidence that every variant has passed. Use the timestamps, HTTP outcomes, and explicit limitations shown in the viewer when explaining a run.

## Shared demo instrumentation

Evidence is teaching instrumentation for the flow viewer, not part of Mervikon's authentication or authorization design and not a production audit log. Observations deliberately omit raw credentials, access/refresh tokens, cookies and proof signatures.

- [Common.Evidence](../src/shared/Common.Evidence/) supplies the shared observation schemas, sanitized projections and publishers.
- [DemoEvidenceExtensions](../src/shared/Common.Evidence/DemoEvidenceExtensions.cs) registers those publishers and observes API request outcomes through `UseDemoEvidence`. [WorkflowEvidence](../src/shared/Common.Security/WorkflowEvidence.cs) adds a bounded description of the completed business result.
- [BffDemoEvidence](../src/apps/shared/demo/BffDemoEvidence.cs) is one shared, demo-only adapter linked into Workplace, Finance Desk and the isolated browser lab. `AddBffDemoEvidence` connects OIDC events and the back channel to the same publishers. Keeping this adapter separate avoids adding an OIDC dependency to every evidence producer. Supplier Portal does not currently attach this adapter.

The adapters differ because inbound API requests and outbound OIDC messages have different observation points. They share delivery and sanitization rather than implementing authentication themselves. With no `Evidence:Endpoint`, publishers do not send observations and the BFF adapter does not attach its OIDC hooks.

Interactive authorization, callbacks and token redemption use the ASP.NET Core OIDC events and back channel. Renewal uses the ProAuth `OidcClient` named HTTP client. The BFF adapter registers its observer on both transports so Demo Console can show a refresh when the SDK sends one.
