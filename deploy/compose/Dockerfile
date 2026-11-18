# syntax=docker/dockerfile:1
FROM node:26.12.1-alpine AS frontend
WORKDIR /source
COPY package.json package-lock.json ./
RUN npm ci --ignore-scripts
COPY tsconfig.json vite.config.ts ./
COPY src/apps ./src/apps
ARG APP=Workplace
RUN MERVIKON_APP=$APP npm run build && if [ "$APP" = DemoConsole ]; then cp -R src/demo-console/DemoConsole/wwwroot /frontend; else cp -R src/apps/$APP.Bff/wwwroot /frontend; fi

FROM mcr.microsoft.com/dotnet/sdk:10.0.404 AS backend
WORKDIR /source
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY --parents src/**/*.csproj src/**/packages.lock.json labs/**/*.csproj labs/**/packages.lock.json ./
ARG PROJECT=src/apps/Workplace.Bff/Workplace.Bff.csproj
RUN --mount=type=secret,id=proauthfeed,required=true dotnet restore "$PROJECT" --locked-mode --configfile /run/secrets/proauthfeed
COPY src ./src
COPY labs ./labs
COPY config/business ./config/business
COPY docs/scenarios/registry.md ./docs/scenarios/registry.md
RUN dotnet publish "$PROJECT" --no-restore -c Release -o /publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0.15-noble AS runtime
# CVE-2026-109518 affects APT itself. Disable retries while installing the vendor fix.
RUN apt-get -o Acquire::Retries=0 update \
    && apt-get -o Acquire::Retries=0 install -y --no-install-recommends --only-upgrade apt libapt-pkg6.0t64 \
    && dpkg --compare-versions "$(dpkg-query -W -f='${Version}' apt)" ge 2.8.4ubuntu0.1 \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
# Public CA trust only; private keys and feed credentials never enter an image layer.
USER root
RUN --mount=type=secret,id=trustedca if [ -f /run/secrets/trustedca ]; then cp /run/secrets/trustedca /usr/local/share/ca-certificates/mervikon.crt && update-ca-certificates; fi
COPY --from=backend /publish ./
ENV ASPNETCORE_HTTP_PORTS=8080
USER $APP_UID
EXPOSE 8080

FROM runtime AS api
ENTRYPOINT ["dotnet"]
CMD ["Procurement.Api.dll"]

FROM runtime AS bff
COPY --from=frontend /frontend ./wwwroot
ENTRYPOINT ["dotnet"]
CMD ["Workplace.Bff.dll"]
