﻿FROM mcr.microsoft.com/dotnet/sdk:8.0-preview AS build-env
WORKDIR /App

# Copy everything
COPY . ./
# Restore as distinct layers
RUN dotnet restore
# Build and publish a release
RUN dotnet publish -c Release -o out -r linux-arm64

# Build runtime image
FROM mcr.microsoft.com/dotnet/aspnet:8.0-preview
WORKDIR /App
COPY --from=build-env /App/out .
ENTRYPOINT ["dotnet", "QueueWorker.dll"]