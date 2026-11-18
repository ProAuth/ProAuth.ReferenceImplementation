# Local BFF session, refresh and logout across replicas

This opt-in local check runs two copies of Workplace BFF using a shared Redis cache for protected session tickets, ProAuth session-scoped token access, distributed refresh locks and an encrypted Data Protection key ring. It shares the existing Workplace client registration and hostname. Procurement remains a single in-memory instance; its storage semantics are unchanged.

The overlay requires the shared Traefik proxy and Docker Compose 2.24.4 or newer for `!override`. Both BFFs must use the same image, configuration and certificate. Never delete shared state or rotate its encryption certificate as an ordinary demo reset.

```sh
node scripts/mervikon.mjs build --service workplace-bff
node scripts/mervikon.mjs compose --bff-replicas -- --profile bff-replicas up -d workplace-bff workplace-bff-replica
```

Use a disposable native Workplace user in a separate browser. Authenticate normally. Ordinary requests use Traefik balancing without sticky sessions. The overlay also provides an explicit `X-Mervikon-Bff-Replica: one` or `two` request header for deterministic testing. This selects an instance only; it grants no authentication, authorization or assurance. `X-Mervikon-Bff-Instance` identifies the actual responding process.

While enabled, the authenticated `/replica-lab/session` observation endpoint returns only instance, token presence and refresh/expiry timestamps. It sets `Cache-Control: no-store` and returns no tokens or identity claims. Decoded expiry is a scheduling observation, not authorization evidence. The endpoint is absent from normal operation. Both instances retain the package-owned cookie, CSRF and proxy controls.

When upgrading to 3.4.0, replace all replicas of a BFF together and have users sign in again. The ProAuth Redis ticket store uses its own key format and Data Protection purposes. Keep the same `ProAuthBff:Redis:TicketStore:ApplicationName`, key prefix and Data Protection ring on every replica of that BFF. Choose distinct application identities and prefixes for other applications or environments sharing Redis.

## Walkthrough

1. Confirm `/api/me` succeeds through each forced instance with the same browser cookie. A missing CSRF header on a mutation must fail on each.
2. Stop only `workplace-bff`. Requests without the selector must continue through the replica. Recreate the first instance and verify that the same session still works.
3. Observe the token's real expiry, then issue concurrent `/api/me` requests split across both instances. Verify successful API responses and the same renewed expiry on both. Do not edit clocks or stored timestamps. Outcomes alone do not prove the exact number of token-endpoint requests.
4. Keep the disposable test's pre-logout cookie in browser automation memory only. Complete the real BFF and ProAuth logout confirmations. Reinstall that same cookie and verify API rejection on both instances, then delete it. This tests server-side removal rather than merely observing that the browser cleared a cookie. Never log or export cookies.

5. Sign in as the same approver in two independent browser profiles with baseline assurance. Verify in profile A only. Profile B must still get an insufficient-authentication challenge on its own high-value purchase. A successful refresh in either profile must preserve its own grant.

## Interpreting results

The same authorized session should work through both instances, survive loss of one instance, and observe a renewed token after natural expiry. Requests missing the required CSRF header must fail. Replaying the saved pre-logout cookie after confirmed logout must fail on both instances.

Record the actual outcomes and timestamps for each run. These controls do not establish exactly one refresh grant, authorization-server-initiated back-channel logout, Redis primary failover, key rotation, or Kubernetes behavior.

## Cleanup

```sh
node scripts/mervikon.mjs compose --bff-replicas -- stop workplace-bff-replica
node scripts/mervikon.mjs compose --bff-replicas -- rm -f workplace-bff-replica
node scripts/mervikon.mjs compose -- up -d --no-deps --force-recreate workplace-bff
```

This removes only the temporary replica and restores normal Workplace routing. Keep shared Redis, product containers, other application sessions and business data. Avoid `--remove-orphans`: other legitimate optional profiles may be running.
