# Alpha release provenance and immutability

## Existing v0.1.0-alpha.1 evidence (legacy, published before this policy)

- Tag: `v0.1.0-alpha.1`, lightweight tag target: `d739002abc99ac9c6adf5edfed0595aa4aa7878b`.
- Successful build: [Actions 37933298925](https://github.com/playCosmos/VirtualCharacterRender/actions/runs/37933298925), both Windows x64 and macOS jobs and release attachment successful for the same commit.
- Published Windows archive SHA-256 (GitHub asset digest): `fa35c62520fe399fb2561c75fdee193675eecf6d631300b33b67fe824c8f7631`.
- Published macOS archive SHA-256 (GitHub asset digest): `c3bfc5daf590367b19163d53dc2e1e0f7ad690b954cfc81d4f6619dec58beb3b`.

The legacy files did not include an independently downloaded and re-hashed provenance manifest. The evidence above establishes matching tag/build SHA and published GitHub asset digests; it does not claim runtime or hardware validation.

**Do not move this tag or overwrite the existing alpha.1 archives.** The old `release/0.1.0-alpha.1` branch is retained for history; its push-based publish/build workflows must be retired independently because GitHub runs workflow files from the pushed branch.

## Policy for new alpha versions

1. Merge reviewed changes into `develop` and record the exact commit SHA. A separate source-free Unity validation pass remains required; mere repository-structure CI success does not imply Unity execution.
2. Create a NEW lightweight tag, e.g. `git tag v0.1.0-alpha.2 <approved-sha>`, then push only that tag. Never retag or republish an existing alpha version.
3. `build-alpha-binaries.yml` runs only for new `v*-alpha.*` tag pushes. Its preflight confirms the tag SHA and rejects a pre-existing GitHub Release.
4. Windows/macOS jobs check out the same tagged commit, bootstrap pinned Unity packages, and produce version-specific archives; each archive carries a SHA-256 sidecar and a source-SHA sidecar for the publish job to verify.
5. Publishing waits for BOTH successful builds. It rechecks the remote tag, source SHA, both checksums, and release absence, then creates the GitHub prerelease exactly once. It publishes `SHA256SUMS.txt` and `build-provenance.json` containing version, source SHA, Unity version, Actions run URL, filenames and digests. It never uses `--clobber`, tag force-update, or release edit.
6. Failed or partial publication requires investigation and a new version/tag. Do not move the old tag or silently replace assets.
7. Validate each release on Windows/macOS players and actual tracking/OBS hardware separately. Build success is not a runtime or performance PASS.

### Repository settings required

Configure a GitHub **ruleset for refs/tags/v*-alpha.*** to restrict **tag updates and deletion**, granting minimal bypasses. A workflow can reject a moved tag during its own run, but cannot itself prohibit an administrator or token with sufficient privileges from force-moving the ref. Protected tags / immutable releases require repository-side enforcement.

### Local build compatibility

`AlphaBuildMenu` defaults to `0.1.0-alpha.1` for legacy local commands. CI supplies the release version through `VCR_RELEASE_VERSION` (for example `0.1.0-alpha.2`) and output folders are derived from it, not baked into the job matrix. Local commands accept the same optional version: `./tools/build-alpha.ps1 -AlphaVersion 0.1.0-alpha.2` and `./tools/run-alpha.ps1 -AlphaVersion 0.1.0-alpha.2`. Local command examples for the existing release remain in `docs/ALPHA_TESTING.md`.
