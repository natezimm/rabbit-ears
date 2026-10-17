# The public demo site: the TV and remote in one container, with every visitor
# getting a TV of their own and clips generated at build time. The box at home
# does not use this; it is installed with scripts/deploy.sh.
#
#   docker build -t rabbit-ears .
#   docker run --rm -p 8080:8080 rabbit-ears     then open http://localhost:8080

FROM node:24-bookworm-slim AS web
WORKDIR /src/web
COPY web/package.json web/package-lock.json ./
RUN npm ci --no-audit --no-fund
COPY web/ ./
# Angular writes into the API's wwwroot, one folder up.
RUN npx ng build

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS api
WORKDIR /src
COPY global.json Directory.Build.props ./
COPY src/ src/
RUN dotnet publish src/RabbitEars.Api -c Release -o /app

# Real shows cannot be put on a public website, so the demo plays clips made here.
FROM debian:bookworm-slim AS clips
RUN apt-get update \
 && DEBIAN_FRONTEND=noninteractive apt-get install -y --no-install-recommends ffmpeg \
 && rm -rf /var/lib/apt/lists/*
COPY scripts/make-dev-media.sh /make-dev-media.sh
RUN bash /make-dev-media.sh /media

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=api /app ./
COPY --from=web /src/src/RabbitEars.Api/wwwroot ./wwwroot
COPY --from=clips /media /srv/rabbitears/media

ENV Urls=http://0.0.0.0:8080 \
    Box__Demo__Enabled=true \
    Box__RescanMinutes=0 \
    Box__StateDir=/tmp/rabbitears \
    Box__Ffprobe="" \
    Box__Picture__Transition=static
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "RabbitEars.Api.dll"]
