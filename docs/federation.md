# Federation availability

The runnable reference uses native ProAuth identities. There is no active upstream federation profile, and the command wrapper rejects `--federation`. Supplier Portal's native sign-in must not be presented as federation.

Disabling a provider does not by itself revoke every existing session or JWT. Remove retired application memberships and close their sessions separately. Never reuse an existing provider or subject mapping for another issuer.
