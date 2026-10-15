ARG BUILD_ID
ARG BUILD_VERSION

# Retrieve and set base image layer
FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine@sha256:f62a272ac1b46e83f56b8ed0416572f31cd1128e2c4a5e63eb34d348e4a36095 AS base
ARG BUILD_ID
ARG BUILD_VERSION
WORKDIR /app

# Create build image for runtime
FROM mcr.microsoft.com/dotnet/sdk:10.0@sha256:35d40304542c8689331f8cab17c65926cdf48fe711e289321d71924b230a7d29 AS build
COPY . .

# Restore as distinct layers
RUN dotnet restore "./CASInterfaceService/CASInterfaceService/cas-interface-service.csproj"
RUN dotnet build "CASInterfaceService/CASInterfaceService/cas-interface-service.csproj" -c Release -o /app/build

# Create publish image layer
FROM build AS publish
ARG BUILD_ID
ARG BUILD_VERSION
COPY . .

# Build and publish a release
RUN dotnet publish "CASInterfaceService/CASInterfaceService/cas-interface-service.csproj" -c Release -o /app/publish

# Build runtime image
FROM base AS final
WORKDIR /app
RUN addgroup --system appgroup && adduser --system --ingroup appgroup appuser

# package is updated to include the new America/Vancouver permanent-offset rule.
RUN apk add --no-cache tzdata

# Accept build args and export as environment variables
ARG BUILD_ID
ARG BUILD_VERSION
ENV BUILD_ID=${BUILD_ID}
ENV BUILD_VERSION=${BUILD_VERSION}

COPY --from=publish /app/publish .

# Write version info to readable files
RUN echo "${BUILD_ID}" > /app/build_id.txt && echo "${BUILD_VERSION}" > /app/version.txt

EXPOSE 8080
ENV ASPNETCORE_URLS=http://*:8080
ENV ASPNETCORE_ENVIRONMENT=Production
RUN chown -R appuser:appgroup /app
USER appuser
ENTRYPOINT ["dotnet", "cas-interface-service.dll"]