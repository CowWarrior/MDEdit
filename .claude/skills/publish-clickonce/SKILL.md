---
name: publish-clickonce
description: Publish a new ClickOnce release of MDEdit to the docs/ GitHub Pages site. Covers the 4-part version-numbering scheme, the ReleaseNotes.md changelog entry that must be written and committed BEFORE publishing, and the publish-script gotchas (full-framework MSBuild, manifest signing, FormatVersion silently dropping Revision). Use whenever publishing, cutting a release, bumping the version, or editing build/Publish-ClickOnce.ps1 or the ClickOnce.pubxml.
---

# Publishing a ClickOnce release

`docs/` is a live GitHub Pages site (`https://cowwarrior.github.io/MDEdit/`) serving a ClickOnce deployment — this is why the repo is public rather than private.

## Version numbering

Versions are 4-part `Major.Minor.Build.Revision`, split between a value stored in the repo and one derived from git:

| Part | Source | Moves when |
| --- | --- | --- |
| Major | `build/published-version.txt` | `Publish-ClickOnce.ps1 -BumpMajor` (resets Minor and Build to 0) |
| Minor | `build/published-version.txt` | `Publish-ClickOnce.ps1 -BumpMinor` (resets Build to 0) |
| Build | `build/published-version.txt` | **every publish** |
| Revision | `git rev-list --count HEAD` | every commit — **never reset by anything** |

`build/published-version.txt` holds the `Major.Minor.Build` of the **last published** release (not the next one). The script increments it, publishes, and writes the new value back only on success — so a failed publish doesn't silently consume a number. Revision is never reset by a Major/Minor bump specifically so the 4-part version keeps increasing no matter what happens to the stored parts: a version that fails to increase strands installed clients on the old build with no error shown anywhere, which this project has already been bitten by once (see the `FormatVersion` note below).

**The publish script must pass `-p:MDEditReleaseVersion=<Major.Minor.Build>` to MSBuild, and this is not optional.** `published-version.txt` deliberately lags — it records the *last* published release and is written back only after a successful publish — so during a publish it still holds the previous value. Without the override, `SetVersionFromGit` reads that stale file and stamps the **assembly** one release behind the **deployment manifest**, which takes its version from `ApplicationVersion` instead. That shipped once: deployment `1.0.1.49` carrying an assembly reporting `1.0.0.49`, so the installed About dialog disagreed with the version ClickOnce had actually installed. ClickOnce updates still worked (they key off the manifest), which is exactly why it was easy to miss. Ordinary builds pass nothing and correctly fall back to the file.

Two consequences worth knowing. Two publishes with no commits between them repeat the Revision (`1.0.1.49`, `1.0.2.49`) — still increasing overall, because Build moved. And a local build reports the *last published* `Major.Minor.Build` with the current commit count, so it matches the shipped version exactly at the publish commit and drifts only in Revision afterwards.

## Publishing a new version — the required order

1. **Update `MDEdit/samples/ReleaseNotes.md`'s "Recent changes" section first**, before running the publish script. Add a `### Version <Major.Minor.Build>` block — the *release* number only, no Revision — for the version about to be published, newest first.
   - **One entry per published release, never per commit.** Several commits' worth of work collapse into a single entry; unpublished work accumulates into the next release's entry rather than getting its own. Revision numbers gap between releases and that is expected.
   - **Keep it to one or two short sentences.** Name what was added; skip the how-it-works, the syntax tour, and the caveats — those belong in the body of the document, if anywhere. A changelog nobody finishes reading is worse than none.
   - Write for users, in the document's voice, using only constructs MDEdit renders today.
   - Changes with no user-visible effect (refactors, tests, doc edits) don't belong there at all.
   - `git log --oneline` since the commit that last touched `build/published-version.txt` is the starting point — that commit *is* the previous publish — but don't paste commit subjects verbatim.
2. Commit that with the feature work. It ships in the payload (see the `Content` item in `MDEdit.csproj`), so it must be right before the script runs. **Commit before publishing** either way: Revision comes from the commit count, so publishing first stamps a version that excludes the work.
3. Run `build/Publish-ClickOnce.ps1` (standalone — see below), adding `-BumpMinor` or `-BumpMajor` when asked for one.
4. Commit and push the changes under `docs/` **plus `build/published-version.txt`**, conventionally `Publish ClickOnce version <full 4-part version>`.

## Script and configuration notes

- The script must be run standalone (not via `dotnet publish`): ClickOnce's `UpdateManifest` task only runs under the full-framework MSBuild bundled with Visual Studio (`MSBuild.exe`, located dynamically), not the cross-platform MSBuild `dotnet` uses (fails with MSB4803 otherwise).
- ClickOnce manifest signing (the separate XML-DSig signature over `MDEdit.application`/`*.manifest`, distinct from the Authenticode signing applied to every Release build by `build/Sign.ps1`) requires an RSA certificate — the same `CN=Maze Code Signing` cert is used, resolved by subject at publish time, never by thumbprint (it rotates).
- The publish script assembles `ApplicationVersion` as `<stored Major.Minor.Build>.<git rev-list --count HEAD>` (see Version numbering above) and passes the full 4-part version directly — **not** via the separate `ApplicationRevision` property. This MSBuild's `FormatVersion` task (`Microsoft.Build.Tasks.Core.dll`, VS 18) silently ignores `Revision` regardless of how many parts `ApplicationVersion` has, so every publish would otherwise produce the identical version `1.0.0.0` and installed clients would never see an update (verified empirically — confirmed and fixed after finding this in practice).
- The deploy is framework-dependent (`SelfContained=false`) — self-contained ClickOnce output is ~350MB, impractical to version in git. This means machines installing MDEdit need the [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) already present; there's no bootstrapper to auto-install it.
- `MDEdit/Properties/PublishProfiles/ClickOnce.pubxml` holds the static settings (URLs, product/publisher name, `SelfContained=false`, etc). It deliberately omits `ManifestCertificateThumbprint` and `ApplicationRevision` — the script supplies both at publish time.
- The script also deletes any `Application Files\MDEdit_*` folder under `docs/` other than the one it just published — ClickOnce publish only ever adds a version folder, never removes superseded ones.
- `.gitattributes` marks everything under `docs/` as binary (`-text`) except `*.html` — git's CRLF normalization would otherwise corrupt the byte-exact hashes ClickOnce embeds in the manifest.
- `docs/index.html` is the hand-maintained install landing page (the only file under `docs/` not regenerated by publish); it links to `MDEdit.application` and notes the .NET 10 Desktop Runtime prerequisite — update it by hand if install instructions change.
- **The pubxml's `FileAssociation` items (`.md`/`.markdown`/`.txt`) do nothing** — verified empirically (no registry keys or event-log activity at all after a fresh install) that ClickOnce's file-association registration was never wired up for Launcher-based .NET Core deployments, despite the tooling accepting the declaration without error. Real file association is handled entirely by `Services/FileAssociationService.cs` instead (see the Architecture section of `CLAUDE.md`); the pubxml entries are dead config left in place mostly as a documented dead end, not because anything depends on them.
