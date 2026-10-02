FROM golang:1.26.8-alpine AS build
WORKDIR /source
ENV CGO_ENABLED=0
ENV GOMAXPROCS=2
ENV GOFLAGS=-p=2
# Keep the Caddy release, rebuild with patched Go/TLS/HTTP dependencies.
RUN --mount=type=cache,target=/go/pkg/mod --mount=type=cache,target=/root/.cache/go-build \
    go mod init testtask-caddy-build && \
    go get github.com/caddyserver/caddy/v2/cmd/caddy@v2.11.4 \
        golang.org/x/crypto@v0.56.0 golang.org/x/net@v0.58.0 google.golang.org/grpc@v1.83.2 && \
    go build -trimpath -ldflags="-s -w" -o /out/caddy github.com/caddyserver/caddy/v2/cmd/caddy

FROM caddy:2.11.4-alpine
RUN apk upgrade --no-cache && rm -f /usr/bin/caddy
COPY --from=build /out/caddy /usr/bin/caddy
