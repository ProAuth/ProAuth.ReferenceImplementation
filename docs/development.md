# Development

## Prerequisites

Use the .NET SDK 10.0.404 selected by `global.json`, runtime 10.0.15, and Node.js 22.12 or newer. Node.js 26.12.1 is the container build version. Docker Compose, trusted local TLS, a compatible ProAuth deployment, and its CLI are required for integration runs. Unit tests do not start or modify ProAuth.

## ProAuth dependency

The applications and CLI use the official ProAuth 3.4.0 release. [NuGet.Config](../NuGet.Config) maps product packages to the [customer feed](https://packages.proauth.net/v3/index.json) and other dependencies to nuget.org. Project lock files record the resolved versions and hashes.

Declare each direct dependency in the consuming project with a versionless `PackageReference`. Set its version in [Directory.Packages.props](../Directory.Packages.props). The ProAuth entries share `ProAuthPackageVersion` from [Directory.Build.props](../Directory.Build.props), which also supplies the version recorded in demo evidence.

The first `npm start` creates the private feed configuration. Fill in the two credential placeholders before building. For a manual setup, create a private copy of `NuGet.Config` at `.local/nuget.customer.config` with owner-only permissions. Add the following section inside `<configuration>` and fill in your customer feed credentials. Docker mounts the same file as a BuildKit secret during package restore and CLI installation.

```xml
  <packageSourceCredentials>
    <ProAuthFeed>
      <add key="Username" value="YOUR_CUSTOMER_FEED_USERNAME" />
      <add key="ClearTextPassword" value="YOUR_CUSTOMER_FEED_PASSWORD" />
    </ProAuthFeed>
  </packageSourceCredentials>
```

```sh
npm ci --ignore-scripts
dotnet restore Mervikon.slnx --locked-mode --configfile .local/nuget.customer.config
npm run check
dotnet build Mervikon.slnx --no-restore --configuration Release
dotnet test Mervikon.slnx --no-build --configuration Release
dotnet format whitespace Mervikon.slnx --no-restore --verify-no-changes
```

`npm run check` checks formatting, repository boundaries, tooling tests, and TypeScript, then builds all four browser applications. Generated assets are ignored. Container builds produce their own frontend assets.

For one frontend, run `npm run build` (Workplace by default), or set `MERVIKON_APP` to `SupplierPortal`, `FinanceDesk`, or `DemoConsole`. The corresponding BFF or Console serves the generated files; authentication still requires the configured backend and issuer.

## Editing

Use `npm run format` for TypeScript, JavaScript, CSS, and supported configuration files. Use `dotnet format whitespace Mervikon.slnx --no-restore` for C#. `.editorconfig` and the pinned Prettier version define the formatting rules.

Keep `Program.cs` focused on explicit composition: show the ProAuth SDK registration, service-specific scope policies and middleware ordering. Put business rules in the domain projects, focused API conventions in `Common.Security`, and reusable browser requests in `src/apps/shared/api.ts`. Do not duplicate token validation or infer authorization from decoded JWTs in application code.

Integration commands and their prerequisites are in [running](running.md), [protocol controls](protocol-controls.md), and [replica controls](replica-controls.md). Local build/test success does not replace those environment-dependent checks.

## Checks without customer feed credentials

Run `npm ci --ignore-scripts` and `npm run check` for the browser applications and tooling. The standalone purchase-policy tests require only packages from nuget.org.

```sh
dotnet restore tests/Procurement.Domain.Tests/Procurement.Domain.Tests.csproj --locked-mode
dotnet test tests/Procurement.Domain.Tests --no-restore --configuration Release
```
