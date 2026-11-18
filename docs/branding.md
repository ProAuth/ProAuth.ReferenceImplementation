# Application sign-in branding

Workplace and Finance Desk use the Mervikon wordmark. Supplier Portal uses a blue partner-facing sign-in design matching its order and sign-out pages. Each registration has its own application-scoped overrides. The warehouse terminal shares the Partner tenant with Supplier Portal and inherits the standard ProAuth views. Tenant-level customizations take precedence over application customizations.

The Liquid layouts, head partials and styles live under `config/proauth/branding/assets/`. ProAuth supplies the authentication forms, language selector, validation and flow state. Review the layouts when upgrading ProAuth.

Normal provisioning imports the branding. To update just the branding in a configured installation, run `node scripts/mervikon.mjs provision-branding`. The manifest validator checks each resource ID, application path and local source file against the reference ownership list.
