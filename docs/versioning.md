# Versioning

## Version Scheme

All projects in the repository share a single version. The version is derived from git tags at build time -- no version numbers are maintained in source files.

| Build context | Version string | Example |
|---------------|---------------|---------|
| Local build | `0.0.0-local` | `0.0.0-local` |
| CI, no tags | `0.0.0-{hash}` | `0.0.0-a3f2c1b` |
| CI, after tag | `{tag}-{hash}` | `0.1.0-a3f2c1b` |
| CI, on tag | `{tag}` | `0.1.0` |

The version is passed to `dotnet build` via `-p:Version=` in CI. `Directory.Build.props` permanently contains `0.0.0-local` as the default for local development.

## Docker Tags

Every build produces a container tagged with `{version}-{hash}`. Additional tags are applied based on context:

| Event | Docker tags |
|-------|-------------|
| Push to main (untagged) | `{version}-{hash}`, `prerelease` |
| Tag on main | `{version}-{hash}`, `X.Y.Z`, conditionally `latest` |
| Tag on release branch | `{version}-{hash}`, `X.Y.Z`, conditionally `latest` |
| Push to release branch (untagged) | No image (build and test only) |

- `latest` only moves forward. A dot release (e.g. `0.1.1`) does not update `latest` if a higher version (e.g. `0.2.0`) already exists.
- `prerelease` always tracks the most recent build from `main`.

## GitHub Releases

- Tagged builds create a full GitHub Release with changelog and server tarball.
- Untagged main builds create a prerelease GitHub Release (tagged `pre/{version}`). Only the 3 most recent prereleases are kept; older ones are automatically cleaned up.

## Release Types

### Minor release

A new feature release from `main`.

```
git tag v0.2.0    # on main
```

### Major release

Same process as minor, different version number.

```
git tag v1.0.0    # on main
```

### Dot release

A patch to a previously released version when `main` has moved ahead.

```
release/0.1 branch created from v0.1.0
  cherry-pick fixes
  tag v0.1.1
```

## Release Process

### Minor or major release

1. Run `./scripts/release.sh` on `main`
   - The script suggests the next version (bumps minor for main, patch for release branches)
   - Accept or override the suggestion
   - The script promotes the `Unreleased` section in `CHANGELOG.md`, commits, and tags
2. Review the commit
3. Push: `git push github main vX.Y.Z`
4. CI builds, tests, publishes the Docker image, and creates a GitHub Release with the changelog

### Dot release

1. Run `./scripts/dot-release.sh`
   - Lists recent tags
   - Select which version to patch
   - Checks out or creates the `release/X.Y` branch
   - Adds an `Unreleased` section to the changelog if needed
2. Cherry-pick fixes onto the branch
3. Push the branch for CI validation: `git push github release/X.Y`
4. Run `./scripts/release.sh`
   - Suggests the next patch version
   - Promotes changelog, commits, tags
5. Push: `git push github release/X.Y vX.Y.Z`
6. CI handles the rest

## Selective Publishing

On tagged releases, CI detects which artifacts changed since the previous tag:

| Artifact | Paths |
|----------|-------|
| Server | `src/Server*/**`, `src/Shared*/**`, `src/plugins/**`, `src/Client.Web/**` |
| Desktop | `src/Client.Core/**`, `src/Client.Desktop/**`, `src/Shared*/**` |
| Android | `src/Client.Core/**`, `src/Client.Android/**`, `src/Shared*/**` |
| iOS | `src/Client.Core/**`, `src/Client.iOS/**`, `src/Shared*/**` |

Only artifacts with source changes are published. Changes to `Client.Core` or `Shared.*` trigger all dependent artifacts.

## Branch Conventions

| Branch | Purpose |
|--------|---------|
| `main` | Trunk. All development happens here. |
| `release/X.Y` | Created on demand from a tag for dot releases. |

Release branches are not pre-created. They are created by `dot-release.sh` when a patch is needed.
