ARG BUILD_ID
ARG BUILD_VERSION

# Retrieve and set base image layer for .NET 10.0 runtime
FROM mcr.microsoft.com/dotnet/runtime:10.0@sha256:0a0e20d12b84b081fb737fddb92d8d205edce9ac2dc62796d2aab30f03804498 AS base
ARG BUILD_ID
ARG BUILD_VERSION
WORKDIR /app

# Create build image for compilation
FROM mcr.microsoft.com/dotnet/sdk:10.0@sha256:8c6edfb26c85153f236d26efebd9ad349aec403583f343f201ee67838b23658d AS build
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
RUN groupadd --system appgroup && useradd --system --gid appgroup --no-create-home appuser

# Accept build args and export as environment variables
ARG BUILD_ID
ARG BUILD_VERSION
ENV BUILD_ID=${BUILD_ID}
ENV BUILD_VERSION=${BUILD_VERSION}

COPY --from=publish /app/publish .

# Write version/build info to files
RUN echo "${BUILD_ID}" > /app/build_id.txt && echo "${BUILD_VERSION}" > /app/version.txt

# Set entrypoint for console application
RUN chown -R appuser:appgroup /app
USER appuser
ENTRYPOINT ["dotnet", "job-scheduling.dll"]
