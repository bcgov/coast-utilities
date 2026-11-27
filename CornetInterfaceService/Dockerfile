ARG BUILD_ID
ARG BUILD_VERSION

# Retrieve and set base image layer
FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine@sha256:21c3f9798416cba4c0576a947c286231cb71ea2884896cb968253f5c9668f930 AS base
ARG BUILD_ID
ARG BUILD_VERSION
WORKDIR /app

# Create build image for runtime
FROM mcr.microsoft.com/dotnet/sdk:10.0@sha256:8c6edfb26c85153f236d26efebd9ad349aec403583f343f201ee67838b23658d AS build
COPY . .

# Restore as distinct layers
RUN dotnet restore "./CornetInterfaceService/CornetInterfaceService/cornet-interface-service.csproj"
RUN dotnet build "CornetInterfaceService/CornetInterfaceService/cornet-interface-service.csproj" -c Release -o /app/build

# Create publish image layer
FROM build AS publish
ARG BUILD_ID
ARG BUILD_VERSION
COPY . .

# Build and publish a release
RUN dotnet publish "CornetInterfaceService/CornetInterfaceService/cornet-interface-service.csproj" -c Release -o /app/publish

# Build runtime image
FROM base AS final
WORKDIR /app
RUN addgroup --system appgroup && adduser --system --ingroup appgroup appuser

# Pick up Alpine security patches (e.g. OpenSSL) newer than the pinned base image.
# tzdata package is updated to include the new America/Vancouver permanent-offset rule.
RUN apk upgrade --no-cache && apk add --no-cache tzdata

# build args export to environment
ARG BUILD_ID
ARG BUILD_VERSION
ENV BUILD_ID=${BUILD_ID}
ENV BUILD_VERSION=${BUILD_VERSION}

COPY --from=publish /app/publish .

# Write version info to inspectable files
RUN echo "${BUILD_ID}" > /app/build_id.txt && echo "${BUILD_VERSION}" > /app/version.txt

EXPOSE 8080
ENV ASPNETCORE_URLS=http://*:8080
ENV ASPNETCORE_ENVIRONMENT=Production
RUN chown -R appuser:appgroup /app
USER appuser
ENTRYPOINT ["dotnet", "cornet-interface-service.dll"]