# See https://aka.ms/customizecontainer to learn how to customize your debug container and how Visual Studio uses this Dockerfile to build your images for faster debugging.

# This stage is used when running from VS in fast mode (Default for Debug configuration)
FROM mcr.microsoft.com/dotnet/aspnet:9.0-bookworm-slim AS base
USER $APP_UID
WORKDIR /app


# This stage is used to build the service project
FROM mcr.microsoft.com/dotnet/sdk:9.0-bookworm-slim AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["Lean/Launcher/QuantConnect.Lean.Launcher.csproj", "Lean/Launcher/"]
COPY ["Lean.Brokerages.InteractiveBrokers/QuantConnect.InteractiveBrokersBrokerage/QuantConnect.InteractiveBrokersBrokerage.csproj", "Lean.Brokerages.InteractiveBrokers/QuantConnect.InteractiveBrokersBrokerage/"]
COPY ["Lean/Engine/QuantConnect.Lean.Engine.csproj", "Lean/Engine/"]
COPY ["Lean/Algorithm.CSharp/QuantConnect.Algorithm.CSharp.csproj", "Lean/Algorithm.CSharp/"]
COPY ["Lean/Algorithm.Framework/QuantConnect.Algorithm.Framework.csproj", "Lean/Algorithm.Framework/"]
COPY ["Lean/Algorithm/QuantConnect.Algorithm.csproj", "Lean/Algorithm/"]
COPY ["Lean/Configuration/QuantConnect.Configuration.csproj", "Lean/Configuration/"]
COPY ["Lean/Logging/QuantConnect.Logging.csproj", "Lean/Logging/"]
COPY ["Lean/Indicators/QuantConnect.Indicators.csproj", "Lean/Indicators/"]
COPY ["Lean/Common/QuantConnect.csproj", "Lean/Common/"]
COPY ["Lean/Compression/QuantConnect.Compression.csproj", "Lean/Compression/"]
COPY ["Lean/AlgorithmFactory/QuantConnect.AlgorithmFactory.csproj", "Lean/AlgorithmFactory/"]
COPY ["Lean/Api/QuantConnect.Api.csproj", "Lean/Api/"]
COPY ["Lean/Brokerages/QuantConnect.Brokerages.csproj", "Lean/Brokerages/"]
COPY ["Lean/Queues/QuantConnect.Queues.csproj", "Lean/Queues/"]
COPY ["Lean/Messaging/QuantConnect.Messaging.csproj", "Lean/Messaging/"]
COPY ["IBAutomater/QuantConnect.IBAutomater/QuantConnect.IBAutomater.csproj", "IBAutomater/QuantConnect.IBAutomater/"]
COPY ["QuantConnect.GRPC/QuantConnect.GRPC.csproj", "QuantConnect.GRPC/"]
COPY ["QuantConnect.MyAlgos/QuantConnect.MyAlgos.csproj", "QuantConnect.MyAlgos/"]
COPY ["Lean/DownloaderDataProvider/QuantConnect.DownloaderDataProvider.Launcher.csproj", "Lean/DownloaderDataProvider/"]
COPY ["Lean/Optimizer.Launcher/QuantConnect.Optimizer.Launcher.csproj", "Lean/Optimizer.Launcher/"]
COPY ["Lean/Optimizer/QuantConnect.Optimizer.csproj", "Lean/Optimizer/"]
COPY ["Lean/Research/QuantConnect.Research.csproj", "Lean/Research/"]
COPY ["Lean/ToolBox/QuantConnect.ToolBox.csproj", "Lean/ToolBox/"]
RUN dotnet restore "./Lean/Launcher/QuantConnect.Lean.Launcher.csproj"
COPY . .
WORKDIR "/src/Lean/Launcher"
RUN dotnet build "./QuantConnect.Lean.Launcher.csproj" -c $BUILD_CONFIGURATION -o /app/build

# This stage is used to publish the service project to be copied to the final stage
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./QuantConnect.Lean.Launcher.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

# This stage is used in production or when running from VS in regular mode (Default when not using the Debug configuration)
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "QuantConnect.Lean.Launcher.dll", "--config", "/config/config.json"]
