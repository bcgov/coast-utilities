ARG BUILD_ID
ARG BUILD_VERSION

# Retrieve and set base image layer
FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine@sha256:27b6b84beeede74fd16886177d360799c8e4299ceadfbd64eef57bafead7878a AS base
ARG BUILD_ID
ARG BUILD_VERSION
WORKDIR /app

# Create build image for runtime
FROM mcr.microsoft.com/dotnet/sdk:10.0@sha256:ed034a8bf0b24ded0cbbac07e17825d8e9ebfe21e308191d0f7421eaf5ad4664 AS build
COPY . .

# Restore as distinct layers
RUN dotnet restore "./AEMInterfaceService/AEMInterfaceService/aem-interface-service.csproj"
RUN dotnet build "AEMInterfaceService/AEMInterfaceService/aem-interface-service.csproj" -c Release -o /app/build

# Create publish image layer
FROM build AS publish
ARG BUILD_ID
ARG BUILD_VERSION
COPY . .

# Build and publish a release
RUN dotnet publish "AEMInterfaceService/AEMInterfaceService/aem-interface-service.csproj" -c Release -o /app/publish

# Build runtime image
FROM base AS final
WORKDIR /app
RUN addgroup --system appgroup && adduser --system --ingroup appgroup appuser

# Accept build args and export as environment variables
ARG BUILD_ID
ARG BUILD_VERSION
ENV BUILD_ID=${BUILD_ID}
ENV BUILD_VERSION=${BUILD_VERSION}

COPY --from=publish /app/publish .

# Write version/build info to files
RUN echo "${BUILD_ID}" > /app/build_id.txt && echo "${BUILD_VERSION}" > /app/version.txt

EXPOSE 8080
ENV ASPNETCORE_URLS=http://*:8080
RUN chown -R appuser:appgroup /app
USER appuser
ENTRYPOINT ["dotnet", "aem-interface-service.dll"]