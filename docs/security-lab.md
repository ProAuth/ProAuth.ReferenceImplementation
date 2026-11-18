# Security lab

`labs/unsafe` contains deliberately unsafe teaching components. They are isolated from the normal application flow and enabled only through explicit Compose/provisioning options. Customers should start with the secure examples under `src`.

The capture-and-replay lab obtains real short-lived lab tokens, captures traffic at its controlled TLS boundary, and attempts replay without the victim's sender key. The normal resource-server validator still makes the acceptance decision. The runner verifies expiry and deletes captures before declaring cleanup complete. See [local setup](running.md#local-demo-console-and-isolated-comparison).

[Protocol controls](protocol-controls.md) cover token formats, introspection and mTLS. [Replica controls](replica-controls.md) exercise atomic DPoP replay storage, outage handling and restart behavior. [Browser controls](conference.md) compare a deliberately exposed SPA token with the BFF boundary.

Use dedicated fictional identities, owned resources and private networks. No collector or replay runner should be publicly accessible. Do not copy deliberately vulnerable browser or capture code into a normal application. A successful comparison explains its particular attacker assumptions; it does not establish general protocol conformance.
