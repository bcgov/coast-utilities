ARG BUILD_ID

# Retrieve and set base image layer
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
ARG BUILD_ID
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
COPY . .

# Build and publish a release
RUN dotnet publish "AEMInterfaceService/AEMInterfaceService/aem-interface-service.csproj" -c Release -o /app/publish

# Build runtime image
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
EXPOSE 8080
ENV ASPNETCORE_URLS=http://*:8080
ENTRYPOINT ["dotnet", "aem-interface-service.dll"]