# Release Guide

## Local Build

```powershell
# Build x64 framework-dependent (default)
.\build-production.ps1

# Self-contained (bundles .NET runtime)
.\build-production.ps1 -SelfContained $true

# Build both x64 and x86
.\build-production.ps1 -Platform all
```

Output: `publish/<platform>/BrickBot.exe` plus `data/languages/*.json`.

## Cutting a Release

```powershell
# Bump minor version (0.1 -> 0.2), build, package as zip
.\release.ps1 -BumpMinor

# Bump major (0.1 -> 1.0), tag automatically
.\release.ps1 -BumpMajor -CreateTag

# Set explicit version
.\release.ps1 -Version "1.0"

# Reuse existing build (don't rebuild, just zip)
.\release.ps1 -SkipBuild
```

`release.ps1` does:
1. Bump (or set) version in `BrickBot/BrickBot.csproj`
2. Run `build-production.ps1`
3. Zip each platform output into `release/BrickBot-vX.Y-<platform>.zip`
4. Generate `release/RELEASE_NOTES.md` from conventional-commit messages
5. Optionally `git tag -a vX.Y`

## Version Conventions

- **Major** bump: breaking changes, large feature additions
- **Minor** bump: new features, non-breaking improvements
- **Patch** bump: bug fixes only (use `-Version "X.Y.Z"` directly)

## Conventional Commit Prefixes (drive release notes)

- `feat:` -> "New Features" section
- `fix:` -> "Bug Fixes" section
- `chore: docs: refactor: style: perf: test: ci:` -> hidden from notes
- anything else -> "Other Changes" section
