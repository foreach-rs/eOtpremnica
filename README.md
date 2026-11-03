# ForEach.Infrastructure.External.eOtpremnica

Client and UBL 2.1 Despatch Advice writer for the Serbian central register of electronic delivery
notes (eOtpremnica). Targets net8.0 and net10.0, no dependencies beyond the framework. The
library's own documentation is in
[src/ForEach.Infrastructure.External.eOtpremnica/README.md](src/ForEach.Infrastructure.External.eOtpremnica/README.md)
and ships inside the package.

Extracted from the HRC solution, where it was consumed by project reference.

## Layout

```
src/ForEach.Infrastructure.External.eOtpremnica/          the library (packable)
tests/ForEach.Infrastructure.External.eOtpremnica.UnitTests/
.github/workflows/ci.yml        build + test + pack check on push to main and on pull requests
.github/workflows/publish.yml   on tag v*: test, pack with the tag's version, push to nuget.org
```

## Releasing

```
git tag v0.2.0
git push origin v0.2.0
```

The tag is the version (`v0.2.0` → `0.2.0`; `v0.2.0-beta.1` → a prerelease). The `<Version>` in
the csproj is only the fallback for a local `dotnet pack`. A push to `main` builds and tests only.

After the first release, set `<PackageValidationBaselineVersion>` in the csproj to that version so
a breaking change against the published API fails the build.

### One-time setup: Trusted Publishing on nuget.org

The publish workflow holds no API key. It authenticates with a GitHub OIDC token that nuget.org
exchanges for a one-hour key, which works only once a policy on nuget.org trusts this workflow:

1. On nuget.org, open your profile menu → **Trusted Publishing** → add a policy, owned by the
   account that owns the `ForEach.*` packages (the `ForEachRS` organisation):
   - Repository Owner: `foreach-rs`
   - Repository: `eOtpremnica`
   - Workflow File: `publish.yml` (the file name only)
   - Environment: leave empty
2. In this repository, Settings → Secrets and variables → Actions → **Variables**, set
   `NUGET_USER` to the nuget.org profile name that owns the policy (`ForEachRS`).

A policy created for a private repository is only provisionally active for seven days until the
first successful publish; push a tag within that window.

## Consuming the package

Published to [nuget.org](https://www.nuget.org/packages/ForEach.Infrastructure.External.eOtpremnica),
so no feed configuration is needed:

```
dotnet add package ForEach.Infrastructure.External.eOtpremnica
```

The publish workflow needs a repository secret named `NUGET_API_KEY`: an API key created on
nuget.org (*Account → API Keys*) with the *Push new packages and package versions* scope and a glob
pattern matching `ForEach.Infrastructure.External.*`. Keys expire; renew them there and update the
secret.

## Local build

```
dotnet build
dotnet test
dotnet pack src/ForEach.Infrastructure.External.eOtpremnica -c Release -o packages
```
