# Public demo media: rights-review and approval procedure

No film, cartoon, audio track, or archive clip is approved merely by being named in the project roadmap. The person preparing a release is responsible for verifying **the exact downloaded file**, not just the original work's title or year. This document is a workflow, not a legal opinion.

## Private inventory (never commit)

Keep original media, a `media-manifest.json`, a rights ledger, and documentary evidence outside Git, such as a private encrypted folder. The version 2 manifest is the machine-checked publishing gate; the rights ledger contains the full human rationale. The repository ignores `demo/private/`, `demo/rights-ledger.json`, and `demo/media-manifest.json`, but safest is to keep all private material outside the checkout.

For **each specific file**, record:

- Exact work title, creator/producer, release year, direct downloaded URL, original source page, and SHA-256 of the downloaded file.
- Claimed rights basis: U.S. public-domain analysis, an express public-streaming license, or official government/public documentation; link evidence, preserve any necessary screenshots and review dates.
- Footage, **soundtrack**, added scores, dubbing, restorations/colorization, subtitles, embedded third-party logos, source-specific notices, and the exact edition downloaded.
- Intended audience/territories (a U.S. public-domain work may be protected elsewhere), whether public streaming and modifications are permitted, and whether commercial/portfolio use is permitted.
- Attribution text, canonical license URL if available, required notices, any adaptation/share-alike requirements, and planned excerpts/edits.
- Reviewer's identity, date, result (`pending`, `approved`, `rejected`), unresolved risks and safer alternatives. Only mark approved after every relevant question has been resolved.

The repository provides **no automatic copyright determination**. Internet Archive hosting, an old release date, or possession of a copy do not by themselves grant streaming rights. Prefer clearly sourced worldwide public-domain/CC0/CC BY recordings and official NASA material with third-party material excluded. Do not assume CC BY-NC permits promotional use; don't trim CC BY-ND material without separate permission. Ask qualified counsel about unresolved territorial and derivative-rights questions.

## Two-stage approval gate

1. Copy `demo/media-manifest.example.json` to a private directory. The example intentionally has `approvedForPublicDemo: false`. Fill `source`, `channel`, `title`, `creator`, `sourceUrl`, `license`, `rightsEvidence`, and the public `modifications` notice with accurate values.
2. Verify the evidence, exact audio/visual edition and intended uses; record a decision in your **private** ledger. Only then set `approvedForPublicDemo: true`, `reviewedBy`, and `reviewedAt` on the private version 2 entry.
3. Run `python3 scripts/prepare-demo-media.py PRIVATE_MANIFEST OUTPUT --validate-only` and `python3 scripts/prepare-demo-media.py PRIVATE_MANIFEST OUTPUT` on an offline/local work machine. The script refuses any unapproved, malformed, or missing-source entry; it never fetches media.
4. Use a **new, clean output directory** per release. Inspect `.demo-media.json` for public-only fields, verify files and hashes, and inspect the credits page. Preserve evidence locally and never copy the private manifest or originals onto the public server.
5. Only then use the versioned, read-only Compose mount procedure in `demo/real-media.md`. Do not change the public demo's port 8620 or include external media inside the Docker image.

`licenseUrl` (when applicable), `year`, and `attribution` are optional public fields. `modifications` is mandatory on version 2 and should disclose the actual conversion/edits. `rightsEvidence`, `reviewedBy`, `reviewedAt`, original `source` paths and private notes **must never appear** in public prepared metadata or the `/api/demo/credits` response. The server only exposes metadata fields explicitly declared in the public credit model.

## Compatibility and enforcement

- **Version 1 private manifest**: still accepted by the preparation script for repeatability; it has no approval gate and should **not** be used for a new public release.
- **Version 1 prepared metadata**: still readable at runtime to avoid breaking an already mounted library. Migrate to version 2, and review every old item before publishing a new release.
- **Version 2 private manifest**: every entry must be explicitly approved, with reviewer/date and modification text. Any pending or rejected entry causes validation and preparation to fail. No partial publish.
- **Version 2 prepared metadata**: only files with an approved entry can enter the demo channel scanner, be served by `/media/{id}`, or appear in credits. Missing, malformed, or unsupported metadata fails closed (empty public lineup). Extra files in the mount are ignored, not served.
- **Generated fallback library**: built by `scripts/make-dev-media.sh` without a prepared manifest; remains usable for development, CI, and rollback.

The approval flag is a **human attestation**, not proof of ownership or a legal opinion. Do not set it just to make tests pass. Phase 3 will add exact excerpt ranges, aspect ratio verification, and additional FFmpeg QA.

## Attribution display

The public demo's expandable **Media credits** section is generated from the prepared media metadata, sorted by channel/title. It displays title, year, creator, original-source link, license basis, optional license link, attribution and transformation disclosure. A missing license URL is not shown as a placeholder. Software/font notices remain in `THIRD-PARTY-NOTICES.md`; the project's MIT license covers code, not third-party footage.

## Publication checklist

- [ ] Confirm evidence for **every** exact video-and-audio edition and territory.
- [ ] Confirm adaptation/excerpt permission and any required on-video notices.
- [ ] Check every source and license link, and public attribution wording.
- [ ] Set explicit approval only after human review; store the signed decision privately.
- [ ] Validate, prepare and inspect new version 2 output locally.
- [ ] Verify no private evidence, reviewer names, source paths or rejected media appear in output.
- [ ] Verify approval allowlisting, credits, channel tune and range-streaming tests.
- [ ] Deploy into a new versioned directory, smoke-test, and retain rollback.

## Helpful starting points

- [Creative Commons attribution guidance](https://wiki.creativecommons.org/wiki/Recommended_practices_for_attribution)
- [Creative Commons license types](https://creativecommons.org/cc-licenses/)
- [NASA image and media usage](https://www.nasa.gov/nasa-brand-center/images-and-media/)
- [Duke public-domain resources](https://cspd.law.duke.edu/publicdomainday/)
