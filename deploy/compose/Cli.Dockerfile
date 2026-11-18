# syntax=docker/dockerfile:1
FROM mcr.microsoft.com/dotnet/sdk:10.0.404 AS install
ARG PROAUTH_CLI_VERSION
RUN --mount=type=secret,id=proauthfeed,required=true dotnet tool install ProAuth.Cli --version ${PROAUTH_CLI_VERSION} --tool-path /tools --configfile /run/secrets/proauthfeed
WORKDIR /source
COPY Directory.Build.props Directory.Packages.props global.json ./
COPY --parents src/clients/Demo.MembershipSetup/*.csproj src/clients/Demo.MembershipSetup/packages.lock.json ./
RUN --mount=type=secret,id=proauthfeed,required=true dotnet restore src/clients/Demo.MembershipSetup --locked-mode --configfile /run/secrets/proauthfeed
COPY src/clients/Demo.MembershipSetup ./src/clients/Demo.MembershipSetup
RUN dotnet publish src/clients/Demo.MembershipSetup --no-restore -c Release -o /setup
FROM mcr.microsoft.com/dotnet/aspnet:10.0.15-noble
# CVE-2026-109518 affects APT itself. Disable retries while installing the vendor fix.
RUN apt-get -o Acquire::Retries=0 update \
    && apt-get -o Acquire::Retries=0 install -y --no-install-recommends --only-upgrade apt libapt-pkg6.0t64 \
    && dpkg --compare-versions "$(dpkg-query -W -f='${Version}' apt)" ge 2.8.4ubuntu0.1 \
    && rm -rf /var/lib/apt/lists/*
COPY --from=install /tools /tools
COPY --from=install /setup /setup
WORKDIR /workspace
ENTRYPOINT ["/tools/proauthcli"]
