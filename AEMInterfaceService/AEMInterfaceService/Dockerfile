ARG BUILD_ID
ARG BUILD_VERSION

# Retrieve and set base image layer
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
ARG BUILD_ID
ARG BUILD_VERSION
WORKDIR /app

# Create build image for runtime
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
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
ENTRYPOINT ["dotnet", "aem-interface-service.dll"]