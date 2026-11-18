# Security boundaries

Use fictional fixtures and dedicated ProAuth resources. Keep TLS verification enabled, validate exact issuers and audiences, and trust only configured proxies. Application membership, scopes, and transaction checks remain authoritative even after authentication succeeds.

`labs/unsafe` intentionally demonstrates insecure behavior. Enable it only in an isolated local environment. Anyone who can reach Demo Console through the demo proxy can read and export its sanitized observations. Keep that route within the intended demo audience. Publishers authenticate on the separate private evidence listener.

The local Compose deployment trusts the Docker host, the Redis service, and containers allowed on each private network. A cache prefix provides naming separation. Use separate Redis ACLs or instances and encrypted transport for independently administered applications, and restrict write access to Data Protection key storage. Pin the reverse proxy address: an address reassigned to another container can transfer trust in forwarded headers to the wrong peer.

Private configuration belongs in `.local/` or another protected location outside version control. Preserve BFF security state and Data Protection certificates across application restarts. Business data is resettable; authentication state is not ordinary demo data.

If you find an unintended security issue, report it privately through your established ProAuth support contact. Include the affected version and a minimal reproduction using fictional data. Do not include live tokens, secrets, personal data, or private logs in a public issue.
