# Deployment boundaries

Docker Compose is the primary local environment. Follow [running](running.md) for the external ProAuth dependency, trusted TLS, owned resource provisioning, stable keys, and environment configuration.

Existing private environment files can override the repository’s Redis and Traefik image tags. Review those overrides when upgrading.

Each business API has one in-memory state owner. Do not increase its replica count without replacing that state with durable concurrency-safe storage. The [BFF replica walkthrough](bff-replicas.md) demonstrates sharing security state independently of business API scaling.

The chart in `deploy/helm/mervikon` covers the initial Workplace/Procurement slice only. It is not a complete deployment of the local application set. Supply private values, pre-created credentials, membership data, TLS, and a cluster enforcing NetworkPolicy. Include the stable Workplace private DPoP JWK in the client Secret under `workplace-dpop-jwk`. Provision identity resources before application startup; provisioning is not a chart hook.

Demo Console, conference controls, and deliberately unsafe services are local operator tools. A public multi-user deployment requires an independent isolation and access-control design. Never publish local evidence ingestion, Redis, lab collectors, or credential files.
