# ProAuth Reference Implementation License

Version 1.0 — 2 November 2026

Copyright © 2026 4tecture GmbH. Contributor copyrights, where identified, remain with their respective owners.

Licensor: 4tecture GmbH, Industriestrasse 25, 8604 Volketswil, Switzerland.
Licensing contact: [info@4tecture.ch](mailto:info@4tecture.ch).

This is a source-available license with restrictions on competitive use. It is not an open-source license and does not grant the unrestricted permissions of the MIT or Apache licenses.

## 1. Scope and acceptance

This agreement governs the ProAuth reference implementation, including its source code, documentation, configuration examples, tests, scripts, and other materials identified as licensed under this agreement (the **Software**). It also governs copies and adaptations of those materials, including compiled forms. Separately licensed components are excluded as described in Section 7.

**Licensor** means 4tecture GmbH. **You** means the individual or legal entity exercising rights under this agreement. If you act for an organization, you must have authority to accept this agreement for that organization. Your employees and contractors may exercise your permissions on your behalf, and you are responsible for their compliance in that work.

By exercising a permission granted by this agreement that you would not otherwise have, you accept its conditions. If you do not accept, this agreement grants you no permission to exercise those rights. It does not restrict rights that applicable law gives you independently of this agreement.

## 2. Definitions

**ProAuth** means the identity and access-management product supplied by Licensor under its own separate product license.

A **ProAuth Application** is a business application or service that genuinely integrates ProAuth for authentication, authorization, federation, or related identity functionality. It may be developed for your organization, for a customer, or for commercial distribution or hosting. It must not be a Competing Identity Offering. Merely including an unused or nominal ProAuth connection does not qualify. An application may support other identity providers alongside a genuine ProAuth integration.

A **Competing Identity Offering** is a product or service whose principal purpose is to supply identity-provider, authentication-server, authorization-server, identity-federation, or identity-management functionality as an alternative to ProAuth. An SDK, library, or framework principally enabling such a substitute also qualifies. An ordinary business application does not qualify merely because it has sign-in, permissions, or account-management features. Competition is determined by the offering's actual functionality and intended use, regardless of price, technology, branding, or deployment model.

A **Competing Reference Offering** is a demonstration, sample application, starter kit, reference implementation, or reusable training package derived from the Software and offered as a substitute for this reference implementation for demonstrating or integrating an identity platform other than ProAuth. Rebranding the Software or replacing its ProAuth integration does not avoid this definition. The educational and reporting activities expressly permitted by Sections 3 and 4 are not Competing Reference Offerings.

## 3. Permissions

Subject to the conditions in this agreement, Licensor grants you worldwide, nonexclusive permission, without a license fee or royalty for the Software, to copy, run, study, modify, display, and distribute the Software for the following purposes:

1. **Learning and evaluation.** Personal study, organizational evaluation, proofs of concept, experimentation, interoperability testing, and security research. These permissions are available to customers, prospective customers, students, conference attendees, and others; no purchase or customer status is required.
2. **Education and demonstrations.** Conferences, classes, workshops, tutorials, articles, and demonstrations explaining ProAuth integration or general identity protocols through this reference implementation. These activities may be free or paid, and may include recordings, screenshots, code excerpts, and distribution of exercise materials.
3. **ProAuth integration.** Developing, testing, operating, maintaining, selling, distributing, or hosting ProAuth Applications, including production applications. You may incorporate and adapt portions of the Software into those applications.
4. **Customer and partner services.** Providing paid or unpaid implementation, customization, support, consulting, and training for the uses permitted above, including work performed by independent partners or contractors for customers.
5. **Collaboration.** Publishing forks, proposing improvements, and sharing original or modified copies for the permitted purposes, subject to Section 5.

You may charge for a permitted application, service, course, or distribution. You are not required to publish your own application source code or private modifications. Public availability of a fork does not expand the permitted purposes.

These permissions apply to uses of the Software, not to a person's or organization's business identity. Working for, or operating, a business that competes with Licensor does not by itself disqualify you from a permitted use. The restrictions in Section 4 still apply to that use.

## 4. Competitive-use restrictions and retained freedoms

Unless Licensor separately authorizes the activity in writing, you must not use, copy, modify, distribute, display, or incorporate the Software, in whole or in part, for any of the following purposes:

1. Developing, operating, providing, or incorporating it into a Competing Identity Offering.
2. Creating or supplying a Competing Reference Offering.
3. Creating or conducting sales demonstrations, promotional material, or customer proofs of concept that use the Software to market or sell a Competing Identity Offering. This restriction applies to public presentations and private demonstrations for prospective customers.
4. Providing others with the Software, modifications, or services specifically to enable an activity prohibited by this section, whether directly or through another person or entity.

These restrictions apply equally to free and paid offerings. Adding attribution, renaming an offering, changing its interface, porting its code, or retaining a nominal ProAuth integration does not authorize a prohibited use.

Independent study, security analysis, interoperability testing, and fair technical comparisons remain permitted. You may publish your findings, including criticism, benchmarks, screenshots, and reasonably necessary code excerpts, with applicable attribution. Temporary changes used solely for such investigation are permitted; this exception does not authorize supplying a reusable competing reference application or conducting the competitive sales demonstrations described above. Paid teaching of general protocols through the Software remains permitted; supplying a competing identity platform's derived demo or starter kit does not become permitted merely by describing it as training.

This agreement does not claim ownership of general ideas, protocols, algorithms, skills, or know-how. It does not prohibit independently developed implementations that do not use material protected by rights licensed under this agreement. Statutory exceptions, including applicable quotation, research, and interoperability rights, remain unaffected.

## 5. Distribution, attribution, and modifications

When you distribute the Software or an adaptation, including as part of another application, you must:

1. Provide a complete copy of this agreement with the distributed materials. For a compiled application, it may accompany the application in its documentation or legal-notices materials.
2. Preserve applicable copyright, license, and attribution notices for the portions included. Clearly identify copied portions in a larger application as originating from the ProAuth reference implementation by 4tecture GmbH and as covered by this agreement.
3. Identify your material changes in the modified files or accompanying release notes. Do not represent a modified version as an unmodified or officially supported Licensor release.
4. Keep the Software and the portions of your adaptations derived from it subject to this agreement. Do not offer those portions under alternative terms that permit uses this agreement prohibits or remove permissions this agreement grants.

You may choose your own terms for independently authored application code and other separable materials. This agreement does not require licensing your entire application under these terms. You may keep source code private; the notice obligations do not create a source-disclosure requirement.

Recipients receive the applicable permissions directly from the relevant rights holders under this agreement. You cannot grant broader rights in the Software on their behalf. Your own application terms must identify and preserve the terms applicable to the included Software. Charging for a copy does not confer an unrestricted license on the recipient.

For a hosted ProAuth Application where users receive no copy of the Software, source distribution is not required. Include the attribution and a copy of this agreement in the service's accessible legal notices. Attribution need not appear on the sign-in screen or in ordinary application branding. Customer-side code delivered to a browser is a distributed copy for purposes of the applicable notice obligations.

## 6. Ownership, patents, and branding

Ownership of the Software remains with its respective rights holders. You retain ownership of your original contributions and independently authored materials, subject to the rights in the underlying Software. Rights not expressly granted are reserved.

For the permitted uses, Licensor also grants a royalty-free, nonexclusive, worldwide license under patent claims it controls that are necessarily infringed by the Software as supplied by Licensor. This grant does not extend to claims infringed only by your additions or combinations with other products. If you initiate patent proceedings alleging that the Software itself infringes a patent, your patent permissions under this agreement end when those proceedings are initiated, except where applicable law requires otherwise.

You may use the names ProAuth and 4tecture to describe origin, compatibility, and the subject of permitted training accurately. You may reproduce branding as it appears in unmodified reference screens and in truthful screenshots or recordings of permitted activities. No broader trademark or logo rights are granted. Do not imply official endorsement, certification, partnership, or support without authorization, and do not adopt Licensor's branding as the branding of your own product.

## 7. ProAuth and third-party components

This agreement covers the reference implementation only. It does not license the ProAuth server, ProAuth product packages, commercial services, or other separately licensed products. Obtain the required product licenses, trial permissions, subscriptions, and credentials independently and comply with their terms. Permission to integrate the reference code into production does not establish that a trial or other ProAuth entitlement permits production use.

Third-party code, libraries, fonts, tools, and other materials retain their own licenses. Their applicable license terms take precedence for those components, and this agreement does not add restrictions to rights granted under those licenses. Preserve their required notices when redistributing them. Separately licensed material does not become subject to this agreement merely because it is referenced, linked, bundled, or included in the same repository.

## 8. Duration and termination

The permissions for a version of the Software continue for the duration of the applicable intellectual-property rights while you comply with this agreement. They do not expire merely because you stop being a customer or attendee. They do not automatically convert to MIT, Apache, or another license. A later published license version does not change the terms of a copy already obtained under this version without your agreement.

If you breach this agreement, your permissions terminate automatically. You must stop the prohibited activity and stop exercising the terminated permissions, except as necessary to correct the breach. You may retain an inactive copy where required by law or for legal records.

For your first breach, permissions are automatically reinstated if you fully correct the breach within 30 days after the earlier of discovering it or receiving written notice of it from Licensor. Correction includes ceasing unauthorized use, correcting notices or distributions where applicable, and taking reasonable steps to remedy the breach's effects. Later breaches or correction after that period require Licensor's written reinstatement. Reinstatement does not erase liability for conduct during a breach.

Termination of your permissions does not terminate a compliant recipient's independently obtained permissions. Provisions concerning restrictions, ownership, retained copies, disclaimers, liability, and dispute resolution continue to apply as relevant after termination.

## 9. Warranty and liability

The Software is an educational reference supplied without a guarantee that it is complete, accurate, secure, available, suitable for production, or suitable for your particular requirements. Some explicitly identified lab components intentionally demonstrate insecure behavior. You are responsible for reviewing, testing, securing, and operating any deployment and for meeting your legal and regulatory obligations.

To the extent applicable law permits, Licensor and contributors disclaim warranties, whether express, implied, or statutory, including assurances about quality, fitness for a particular purpose, and absence of infringement. They have no obligation under this agreement to provide maintenance, security updates, support, or indemnification.

To the extent applicable law permits, Licensor and contributors are not liable for losses arising from the Software or this agreement, including data loss, interruption, lost revenue or profit, and indirect or consequential damage, regardless of the legal basis of the claim or prior notice of the possibility of loss. Nothing in this agreement excludes liability for intentional misconduct, gross negligence, or any other liability that cannot lawfully be excluded or limited.

A separate signed support or commercial agreement may impose additional obligations on its parties. You may offer your own support or warranties for a permitted application, but must do so in your own name and without imposing obligations on Licensor or contributors.

## 10. Contributions

When you intentionally submit a contribution for inclusion in the Software, you grant Licensor and recipients permission to use and distribute that contribution under this agreement, including a patent license of the scope described in Section 6 for patent claims you control that are necessarily infringed by your contribution. You confirm that you have authority to grant those permissions and identify any separately licensed third-party material you submit.

You retain ownership of your contribution. Submission does not assign your copyright or grant an unrestricted right to relicense your contribution. Separate contribution terms require express written agreement. Merely discussing an idea or reporting an issue does not transfer ownership of unrelated materials.

## 11. Governing law and general provisions

Swiss substantive law governs this agreement, excluding its conflict-of-laws rules and the United Nations Convention on Contracts for the International Sale of Goods. Subject to mandatory jurisdiction rules, disputes are subject to the exclusive jurisdiction of the competent courts in the Canton of Zurich, Switzerland. Mandatory protections and legal rights that cannot be waived by agreement, including applicable consumer protections, remain unaffected.

If a provision is unenforceable, the remaining provisions continue to apply to the extent legally possible. Failure to enforce a provision on one occasion is not a waiver of it. This agreement is the complete set of license terms for the Software unless an authorized rights holder expressly agrees otherwise in writing. Informal descriptions, repository summaries, and marketing statements do not modify it. The English text governs; translations are informational unless expressly agreed otherwise.

## 12. Additional permissions

For a use outside these permissions, contact [info@4tecture.ch](mailto:info@4tecture.ch), identify the Software version and the intended use, and obtain written permission before proceeding. Only an express written authorization from a rights holder with authority to grant it creates an exception. Customer status, a ProAuth product license, attendance at an event, or silence in response to a request does not by itself grant additional rights in the Software.
