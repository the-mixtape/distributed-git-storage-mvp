FROM golang:1.24-bookworm AS build

RUN git clone --depth 1 --branch v18.3.6 https://gitlab.com/gitlab-org/gitaly.git /src/gitaly
COPY sidechannel-gateway/main.go /src/gitaly/cmd/poc-sidechannel-gateway/main.go
WORKDIR /src/gitaly
RUN CGO_ENABLED=0 go build -trimpath -o /out/sidechannel-gateway ./cmd/poc-sidechannel-gateway

FROM gcr.io/distroless/static-debian12:nonroot
COPY --from=build /out/sidechannel-gateway /sidechannel-gateway
EXPOSE 8090
ENTRYPOINT ["/sidechannel-gateway"]
