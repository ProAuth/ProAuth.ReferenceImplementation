# Local token and certificate controls

These executable SCN-10/15/16 controls use the real ProAuth token, revocation and introspection endpoints, the released client/resource-server packages described in the [integration guide](development.md), and a fixed calibration-station quote. The separate `mervikon-protocols` tenant and its five owned clients are provisioned only with `--protocols`. They have no application business permissions. Every command accepts `--env-file`.

```sh
node scripts/mervikon.mjs configure-workflows
node scripts/mervikon.mjs configure-protocols
node scripts/mervikon.mjs provision --protocols
node scripts/mervikon.mjs build --service protocol-api
node scripts/mervikon.mjs build --service protocol-probe
node scripts/mervikon.mjs compose --protocols -- up -d protocol-api
node scripts/mervikon.mjs protocol-run --variant jwt
node scripts/mervikon.mjs protocol-run --variant opaque
node scripts/mervikon.mjs protocol-run --variant jwe
node scripts/mervikon.mjs protocol-run --variant mtls
```

Allow the new route to become ready before running probes. Each probe requires a valid positive request and then waits for the dedicated 60-second token to expire. A failure exits nonzero; no synthetic success is exported. Sanitized completed results are in the private environment's `evidence/protocol-{variant}.json`. Choose **Protocol controls** in Demo Console for the recorded comparison and safe export.

## Certificate trust boundary

This profile currently requires the shared local ProAuth Traefik deployment, its `proauth-client-cert@file` middleware and `proauth@docker` service. The reference adds a router only for its own tenant path and API hostname. The middleware removes caller-supplied certificate headers and forwards the certificate actually presented over TLS. Both the AS and API trust the exact configured IPv4 proxy and its IPv4-mapped IPv6 representation. `TRUSTED_PROXY_IP` must be that proxy's actual IPv4 address. Do not widen trust to a whole bridge network to hide an address mismatch.

The independent mTLS certificate is generated once and preserved. The token endpoint authenticates it using `self_signed_tls_client_auth`; the API verifies the resulting certificate binding. The probe tests a valid certificate, no certificate, a different self-signed certificate, and a forged forwarded-certificate header without a TLS certificate. TLS server-certificate verification stays enabled throughout. Normal BFFs use their own independent configuration.

## Token representations

JWT access tokens are validated locally. Opaque tokens require authenticated introspection on every request; no response cache is configured. The API validates the signed response's issuer, introspection-client audience, signature, `token-introspection+jwt` type and recent `iat` (30 seconds old at most, five seconds future tolerance). The ProAuth envelope profile adds five seconds of clock skew to its 25-second maximum age and applies the same tolerance to optional response `nbf` and `exp`. This envelope freshness policy is separate from the access token's mandatory expiry, audience, issuer and scope checks.

The introspection-only registration cannot use user-login or client-credentials grants. Signed-envelope tests reject wrong keys, unsigned input, wrong issuer/audience/type, missing/stale `iat` and expired envelopes. The wrapper waits for the actual HTTPS health route before acquiring a token.

The encrypted-token client has a separate RSA encryption key set. Only its public key is provisioned into ProAuth; only the protocol API receives the private decryption JWK. The published resource-server handler decrypts the nested JWE and validates the inner signed token. A modified ciphertext must fail. The selected product defaults are RSA-OAEP and A256CBC-HS512; these demonstrate actual product behavior, not a new algorithm policy. Encryption hides claims and protects ciphertext integrity; it does not turn a bearer token into proof of possession.

ProAuth 3.4.0 validates the envelope using the configured issuer, audience and discovery signing keys. An envelope can omit `exp` because its required `iat` bounds freshness. The quote API still checks the introspected access token’s own expiry. [RFC 9701 section 5](https://www.rfc-editor.org/rfc/rfc9701.html#section-5)

The discovery client preserves authority paths and validates the exact issuer. Credential endpoints come from validated issuer metadata. Revocation response parsing errors fail the probe.

## Interpreting results

A valid request must succeed before a negative control is meaningful. Invalid tokens, expired tokens, tampered ciphertext, and missing or mismatched sender certificates must be rejected. The probe exports actual HTTP outcomes and timestamps; it does not substitute expected values.

Opaque-token introspection can observe revocation immediately. Locally validated JWTs can remain usable until expiry, including encrypted or certificate-bound JWTs. That behavior does not prove an authorization-server grant is still active. Encryption-key/certificate rotation and authorization-server outages require separate controls.
