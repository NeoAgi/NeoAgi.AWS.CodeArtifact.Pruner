# Pull our base image
FROM mcr.microsoft.com/dotnet/sdk:7.0 AS build

COPY . /src/app
WORKDIR /src/app

# Build the Application
RUN dotnet restore NeoAgi.AWS.CodeArtifact.Pruner/NeoAgi.AWS.CodeArtifact.Pruner.csproj
RUN dotnet build NeoAgi.AWS.CodeArtifact.Pruner/NeoAgi.AWS.CodeArtifact.Pruner.csproj -c Release -r linux-x64 --self-contained -o /app/build

# Publish the Application
FROM build AS publish
RUN dotnet publish NeoAgi.AWS.CodeArtifact.Pruner/NeoAgi.AWS.CodeArtifact.Pruner.csproj -c Release -r linux-x64 --self-contained -o /app/release

# Build our Distroless Layer
FROM gcr.io/distroless/dotnet AS final
COPY --from=publish /app/release /opt/release

# Finally Execute our Container
WORKDIR /opt/release
ENTRYPOINT [ "dotnet", "NeoAgi.AWS.CodeArtifact.Pruner.dll" ]