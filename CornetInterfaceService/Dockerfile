ARG BUILD_ID

# Retrieve and set base image layer
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
ARG BUILD_ID
ARG BUILD_VERSION
WORKDIR /app

# Create build image for runtime
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
COPY . .

# Restore as distinct layers
RUN dotnet restore "./CornetInterfaceService/CornetInterfaceService/cornet-interface-service.csproj"
RUN dotnet build "CornetInterfaceService/CornetInterfaceService/cornet-interface-service.csproj" -c Release -o /app/build

# Create publish image layer
FROM build AS publish
ARG BUILD_ID
COPY . .

# Build and publish a release
RUN dotnet publish "CornetInterfaceService/CornetInterfaceService/cornet-interface-service.csproj" -c Release -o /app/publish

# Build runtime image
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
EXPOSE 8080
ENV ASPNETCORE_URLS=http://*:8080
ENTRYPOINT ["dotnet", "cornet-interface-service.dll"]