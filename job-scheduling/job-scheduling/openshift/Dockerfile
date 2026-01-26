ARG BUILD_ID
ARG BUILD_VERSION

# Retrieve and set base image layer for .NET 10.0 runtime
FROM mcr.microsoft.com/dotnet/runtime:10.0 AS base
ARG BUILD_ID
ARG BUILD_VERSION
WORKDIR /app

# Create build image for compilation
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .

# Restore as distinct layers
RUN dotnet restore "job-scheduling/job-scheduling/job-scheduling.csproj"
RUN dotnet build "job-scheduling/job-scheduling/job-scheduling.csproj" -c Release -o /app/build

# Create publish image layer
FROM build AS publish
ARG BUILD_ID
ARG BUILD_VERSION

# Build and publish a release
RUN dotnet publish "job-scheduling/job-scheduling/job-scheduling.csproj" -c Release -o /app/publish /p:UseAppHost=false

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

# Set entrypoint for console application
ENTRYPOINT ["dotnet", "job-scheduling.dll"]
