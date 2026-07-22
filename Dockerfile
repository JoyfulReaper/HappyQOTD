FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build

WORKDIR /src

COPY . .

RUN apk add --no-cache \
    clang \
    build-base \
    zlib-dev

RUN dotnet restore HappyQOTD.slnx

RUN dotnet publish HappyQOTD/HappyQOTD.csproj \
    --configuration Release \
    --runtime linux-musl-x64 \
    --self-contained true \
    /p:PublishAot=true \
    --no-restore \
    --output /app/publish

FROM mcr.microsoft.com/dotnet/runtime-deps:10.0-alpine AS final

WORKDIR /app

RUN apk add --no-cache curl

COPY --from=build /app/publish .

ENTRYPOINT ["./HappyQOTD"]