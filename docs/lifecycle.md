# Identity lifecycle

The reference uses native ProAuth accounts. The runnable purchase journey does not configure SCIM synchronization.

Directory provisioning and business authorization are separate. APIs bind the verified issuer, subject and tenant to an operator-owned membership file and read it on each business decision. Removing a membership takes effect without restarting the API. Directory deactivation alone does not prove immediate rejection of a previously issued JWT.
