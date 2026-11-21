# Releasing

<sub>[Tiếng Việt](releasing.vi.md)</sub>

Releases are built by GitHub Actions from a version tag and land as a **draft**. Nothing is public until a maintainer publishes the draft.

## Steps

1. **Bump the version** in `src/LunaDns.App/LunaDns.App.csproj` (`<Version>x.y.z</Version>`). Every package reads it from there.
2. **Write release notes** at `docs/release-notes/vx.y.z.md`. They become the Release body. Keep them short: what changed, which file to download, known warnings (unsigned exe, SmartScreen, Smart App Control).
3. Commit, then tag and push:

```bash
git tag vx.y.z
git push origin main vx.y.z
```

4. **Wait for the `release` workflow** (`.github/workflows/release.yml`):
   - Job `release-macos` (`macos-latest`): checks the tag is `vX.Y.Z`, builds with `-warnaserror`, runs Core, Platform.MacOS and App (`net8.0`) tests, runs `build/publish-macos.sh`.
   - Job `release` (`windows-latest`): checks the tag matches `<Version>` and that the release notes file exists, builds, runs Core, Platform.Windows and App (`net8.0-windows`) tests, runs `build/publish.ps1`, then creates the draft with `gh release create --draft`.
   - Versions starting with `0.` are marked **prerelease** automatically.
5. **Test the draft assets by hand.** Download them (you are signed in as a maintainer, so you can see the draft), check `SHA256SUMS.txt`, and install on a clean machine. Windows Sandbox (`build/sandbox/LunaDNS.wsb`) works for the Setup exe and the portable zip.
6. **Publish:**

```bash
gh release edit vx.y.z -R lunavo/LunaDNS --draft=false
```

Optionally drop the prerelease flag and mark it latest:

```bash
gh release edit vx.y.z -R lunavo/LunaDNS --prerelease=false --latest
```

## Assets

| File | Built by |
|---|---|
| `LunaDNS-Setup-x.y.z.exe` | `build/publish.ps1` + Inno Setup (`build/installer/LunaDNS.iss`) |
| `LunaDNS-x.y.z-portable.zip` | `build/publish.ps1`; includes `build/HUONG-DAN.txt` with `{VERSION}` filled in |
| `SHA256SUMS.txt` | `build/publish.ps1` |
| `LunaDNS-x.y.z-macos-arm64.dmg`, `…-macos-x64.dmg` | `build/publish-macos.sh`; includes `build/HUONG-DAN-macOS.txt` |
| `SHA256SUMS-macos.txt` | `build/publish-macos.sh` |

GitHub also adds `Source code (zip)` and `Source code (tar.gz)` to every release. They contain no binaries. The [quick start](../guide/en/00-quick-start.md) warns users not to pick them.

## Things that bite

- **Drafts are invisible.** Only maintainers see a draft. Users opening the Releases page see nothing, and the in-app update check ignores drafts. If someone reports "no release", check `gh release list -R lunavo/LunaDNS`.
- **Tag and `<Version>` must match**, and `docs/release-notes/vx.y.z.md` must exist, or the Windows job fails before building.
- **A re-run needs a new tag** or deleting the old tag and draft first: `gh release create` fails if the release already exists.
- The in-app checker compares against the highest version among non-draft releases, prerelease included.

## Signing (not done yet)

- **Windows:** the exe and Setup are not Authenticode-signed, so SmartScreen warns and Smart App Control on Windows 11 blocks them. The workflow has a placeholder for SignPath: sign `LunaDNS.exe` before Inno Setup packages it, then sign the Setup.
- **macOS:** builds are ad-hoc signed, not notarized. With an Apple Developer ID: sign with `codesign --options runtime` plus the .NET JIT entitlements, then `xcrun notarytool submit --wait` and `xcrun stapler staple` on each `.dmg`.
