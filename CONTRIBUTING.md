# Contributing

Keep examples small enough to read and complete enough to copy responsibly. Business authorization belongs in the domain projects and API boundaries; protocol machinery belongs in the supported ProAuth packages. Keep startup files focused on composition, expose ProAuth registrations and service-specific policies, use responsibility-based names, and explain security decisions where they occur. Source comments should connect caller authority, configuration, SDK validation and remaining business checks for developers, architects and tech leads. Prefer named endpoint handlers over compressed mappings of distinct security operations. Use Mervikon for company-owned identifiers; reserve “reference implementation” for the repository description.

Use the formatting and validation commands in [development](docs/development.md). A behavior change needs focused tests covering successful and rejected cases, including organization boundaries, stale revisions, and concurrent writes where relevant. Formatting-only changes do not need new behavioral tests.

Keep public documentation focused on current behavior and reproducible setup. Internal reviews, product roadmaps, research, presentation assets, and recording tools belong outside this repository. Never commit private environment files, tokens, credentials, logs, generated browser bundles, or recorded media.

Dependency upgrades must update the selected versions and lock files together, then repeat relevant integration controls against the matching server. Do not silently replace an unavailable product capability with weaker validation.

Before publication, review the export for credentials and private material and confirm compliance with the [repository license](LICENSE.md) and third-party licenses. Review the commits reachable from the branch you plan to publish as well as its current files.

## Licensing contributions

Contributions intentionally submitted for inclusion are governed by Section 10 of the [ProAuth Reference Implementation License](LICENSE.md). Submit only material you have authority to license, identify third-party material and its terms, and preserve required notices. You retain your copyright; submission is not a copyright assignment or unrestricted relicensing permission. Separate contribution terms require express written agreement.
