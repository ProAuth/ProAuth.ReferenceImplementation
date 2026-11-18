# Local cross-replica replay and outage controls

This SCN-22 variant uses two stateless quote APIs inside the **intentionally unsafe** isolated capture lab. It does not scale the in-memory business services. Both API processes register the published `ProAuth.Oidc.ResourceServer.Redis` store with the same application name, issuer and Redis database. The store atomically claims a proof identity with expiry. Redis stores a hash derived from the application name, issuer, client, proof-key thumbprint, proof ID and endpoint.

For an upgrade from the application-owned store, stop both old replicas and allow the full proof acceptance window (including clock tolerance) to elapse before serving traffic from 3.4.0. The two stores use different Redis keys. A rolling deployment would allow the same proof to be consumed once in each store.

Configure and provision the lab first as described in [running](running.md#local-demo-console-and-isolated-comparison), then:

```sh
node scripts/mervikon.mjs build --service lab-api
node scripts/mervikon.mjs build --service lab-capture
node scripts/mervikon.mjs build --service lab-victim
node scripts/mervikon.mjs replica-run
```

Every command accepts `--env-file`. The wrapper starts only the isolated lab services, takes the same exclusive lock as `lab-run`, and stops them on exit. Do not overlap manual lab Compose commands with either wrapper. No ports or reverse-proxy routes are published. Redis has its own internal network and `lab-replay-state` volume; normal application Redis is not stopped or modified.

The probe requests one real short-lived DPoP access token. It verifies both actual API instance labels, sequential replay across instances, and sixteen concurrent uses of one fresh proof split between the processes. It then asks the operator wrapper to stop the dedicated Redis process. Both APIs must return 503; there is no process-local fallback. The wrapper restarts Redis, and fresh requests must succeed while a previously consumed proof remains rejected. All positive recovery requests use the same still-valid token, so expiry cannot explain that replay rejection.

Redis runs as its existing non-root user, with AOF persistence, `appendfsync always` and `noeviction`. State is retained for the proof-validation window across ordinary restarts. Storage loss, restoring stale snapshots, primary failover and replicated-store consistency are separate operational cases; this test does not establish those guarantees. Do not delete replay state while still-valid proofs exist.

## Observed on 27 October 2026

| Control | Actual result |
| --- | --- |
| First proof on API one | 200 |
| Same proof on API two | 401 |
| Fresh proof on each API | 200 / 200 |
| Sixteen concurrent requests with one new proof | One 200; fifteen 401 |
| Dedicated Redis stopped | 503 / 503 |
| Fresh proof after Redis restart | 200 / 200 |
| Previously consumed proof after restart | 401 |
| Token expiry and capture deletion | Verified |

The wrapper exports only validated scalar observations to the private environment's `evidence/replicas.json` and updates the sanitized Console comparison. Choose **Protocol controls** in Demo Console. Results are recorded evidence, not a readiness indicator. The regular bearer/DPoP view remains the independently recorded single-process comparison.

The separate [BFF replica check](bff-replicas.md) verifies shared sessions, failover, natural-expiry refresh and logout. Distributed business persistence, Redis failover and Kubernetes parity remain separate variants. This result does not complete all of SCN-22.
