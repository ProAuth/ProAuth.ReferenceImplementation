# Scenario index

Stable lesson identifiers used by Demo Console. These labels organize the available reference paths; they are not a certification or a claim of complete variant coverage. Consult the linked operational guides for prerequisites and limits.

| ID | Name | Capabilities | Profiles |
| --- | --- | --- | --- |
| SCN-01 | Request and approve a purchase | OIDC, scopes, business authorization | baseline |
| SCN-02 | Browser token exposure and BFF | Browser token isolation | baseline, unsafe-lab |
| SCN-04 | Buyer and supplier access with native identities | Organization membership, supplier assignment | baseline; federation unavailable |
| SCN-07 | Passkeys recovery and credentials | Passkeys, account navigation | enterprise |
| SCN-08 | Fresh and stronger authentication | Authentication strength and freshness | assurance, enterprise |
| SCN-09 | Workload identities | Client credentials, workload permissions | machine, enterprise |
| SCN-10 | mTLS and certificate binding | mTLS, certificate-bound tokens | mtls, unsafe-lab |
| SCN-11 | Bearer theft and DPoP | DPoP, replay rejection | baseline, dpop, unsafe-lab |
| SCN-12 | Financial action with FAPI | PAR, private_key_jwt, DPoP | fapi |
| SCN-13 | Constrained multi-hop exchange | Token exchange, actor and audience constraints | fapi, exchange-constrained |
| SCN-14 | Refresh and reuse | Refresh rotation and reuse detection | baseline, unsafe-lab |
| SCN-15 | Logout disable and revocation | Logout and token revocation | baseline, enterprise |
| SCN-16 | Token representations | JWT, opaque tokens, signed introspection, JWE | token-formats |
| SCN-18 | Warehouse device authorization | Device authorization | device |
| SCN-22 | Distributed security and deployment | Distributed replay storage and outage behavior | operations |

Start with the [business walkthrough](../walkthrough-local.md), [API assurance](../api-assurance.md), [account security](../account-security.md), [protocol controls](../protocol-controls.md), and [replica controls](../replica-controls.md). Recovery, upstream federation, and SCIM synchronization are not runnable demonstrations. FAPI client settings do not establish conformance certification.
