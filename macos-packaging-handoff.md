# macOS packaging — session handoff

Continuing the signed + notarized macOS packaging for **Fido**
(`github.com/seankearon/fido`) on branch **`feature/macos-packaging`** (open PR **#4**).
The CI side is done; what remains is a one-time certificate step that must happen on a Mac.

---

## Prompt for Claude Code (on the Mac)

> I'm continuing work on the Fido repo (github.com/seankearon/fido) — finishing the
> signed + notarized macOS packaging on branch `feature/macos-packaging` (open PR #4).
>
> Background: the Release (macOS) GitHub Actions workflow builds/signs/notarizes a .dmg
> via Avalonia Parcel. It's been failing notarization because the
> APPLE_DEVELOPER_ID_P12_BASE64 repo secret held the WRONG cert type — an "Apple
> Development" cert, not a "Developer ID Application" cert. Apple never notarizes Apple
> Development certs, so no CI change can fix it. The CI side is already done: a previous
> session pushed commit 181fd35 which (a) rebuilds the .p12 with Apple's full cert chain
> bundled and (b) fails fast printing the leaf subject if it isn't a Developer ID
> Application cert. Full details are documented in `build.md` under "Packaging the macOS
> app".
>
> What I need to do now, ON THIS MAC:
> 1. Create a "Developer ID Application" certificate at developer.apple.com (needs a paid
>    Apple Developer Program membership), export key+cert as developer_id.p12 with a
>    password. Steps are in build.md ("Creating the Developer ID certificate").
> 2. Verify it's the right type before uploading — build.md has the openssl one-liner;
>    the CN must read "Developer ID Application: <Name> (<TEAMID>)".
> 3. Update the two repo secrets:
>    - APPLE_DEVELOPER_ID_P12_BASE64  (base64 -i developer_id.p12 | tr -d '\n')
>    - APPLE_DEVELOPER_ID_P12_PASSWORD
>    IMPORTANT: the cert's TEAMID must match the existing APPLE_TEAM_ID secret, and the
>    APPLE_NOTARY_APPLE_ID account must belong to that same team. (The old bad cert was
>    team W9XFP7XY4L / "Atlantic Business Solutions Ltd", but build.md/the bundle id
>    reference "Shine Forms" — confirm which paid-membership team I'm actually using and
>    make all three line up.)
> 4. Trigger a test run and watch it:
>    gh workflow run release-macos.yml --ref feature/macos-packaging -f version=0.0.1-test4
>    gh run watch   (the workflow only triggers on v* tags or manual dispatch)
> 5. If it goes green, the .dmg is attached as a build artifact (and to the GitHub
>    release on real v* tags). Then we can merge PR #4.
>
> Fallback if no paid membership is available: set
> MacOsSettings.SigningCredentialsType to "AdHoc" in src/Fido.parcel — the app builds and
> runs but users must right-click → Open the first time (no notarization).
>
> Also still open (non-fatal): Parcel logs "Failed to convert fido-icon-1024 icon to
> .Icns format" so the app currently ships without its icon — AppIcon points at
> ../assets/png/fido-icon-1024.png. Worth fixing while we're here.
>
> Start by checking out the branch and reading build.md, then walk me through step 1.

---

## Quick reference (verified facts)

| Thing | Value |
|---|---|
| Repo | `github.com/seankearon/fido` |
| Branch / PR | `feature/macos-packaging` → PR **#4** (OPEN, base `main`) |
| CI fix already pushed | commit **`181fd35`** (re-chains .p12 + fails fast on wrong cert type) |
| Last run | failure, 2026-06-24 — the validation run that caught the wrong cert |
| Bad cert in secret | `Apple Development: Sean Kearon (W9XFP7XY4L)`, O=Atlantic Business Solutions Ltd |
| Secrets already set | `APPLE_DEVELOPER_ID_P12_BASE64`, `…_PASSWORD`, `APPLE_NOTARY_APPLE_ID`, `APPLE_NOTARY_PASSWORD`, `APPLE_TEAM_ID`, `PARCEL_LICENSE_KEY` |
| Trigger | `gh workflow run release-macos.yml --ref feature/macos-packaging -f version=0.0.1-test4` |
| Fallback | `MacOsSettings.SigningCredentialsType` → `"AdHoc"` in `src/Fido.parcel` |

### Two things to watch on the Mac

1. **Team consistency** — the new Developer ID Application cert, the `APPLE_TEAM_ID`
   secret, and the notary Apple ID must all belong to the *same* paid-membership team.
   There's an unresolved naming discrepancy (old cert = "Atlantic Business Solutions Ltd
   / W9XFP7XY4L"; docs/bundle id = "Shine Forms" / `com.shineforms.fido`) — confirm which
   team actually holds the paid membership and make all three line up.
2. Everything is **already documented in `build.md` on the branch**, so on the Mac you
   mostly just need to read it — the prompt points CC there.

### Done already (CI side, commit 181fd35)

- The workflow rebuilds the `.p12` with Apple's full chain bundled (rcodesign embeds only
  what's in the `.p12`, so a Keychain export missing the *Developer ID Certification
  Authority* intermediate would otherwise fail too).
- It **fails fast**, printing the leaf subject, if the cert isn't a Developer ID
  Application cert (caught the wrong cert in ~16s).
- `build.md` documents the full requirement, cert-creation steps, and a local `.p12`
  inspection command.
