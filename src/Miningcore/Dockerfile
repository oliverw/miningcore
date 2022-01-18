#See https://aka.ms/containerfastmode to understand how Visual Studio uses this Dockerfile to build your images for faster debugging.

FROM mcr.microsoft.com/dotnet/aspnet:5.0 AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:5.0 AS build
WORKDIR /src
COPY ["src/Miningcore/Miningcore.csproj", "src/Miningcore/"]
RUN dotnet restore "src/Miningcore/Miningcore.csproj"
RUN apt-get update -y && apt-get install -y procps sudo
#RUN useradd -m docker && echo "docker:docker" | chpasswd && adduser docker sudo
RUN apt-get update -y && \
    apt-get install -y --no-install-recommends cmake build-essential libssl-dev libsodium-dev pkg-config libboost-all-dev libzmq5 && rm -rf /var/lib/apt/lists/*
COPY . .
WORKDIR "/src/src/Miningcore"
RUN dotnet build "Miningcore.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "Miningcore.csproj" -c Release --framework net5.0 -o /app/publish
COPY ["src/Miningcore/config.json", "/app/publish/"]

FROM base AS final
WORKDIR /app
RUN apt-get update -y --no-install-recommends && apt-get install -y procps libboost-all-dev libsodium-dev libzmq3-dev libzmq5
RUN mkdir logs
COPY --from=publish /app/publish .
EXPOSE 3072 3073 4000 6000
ENTRYPOINT ["dotnet", "Miningcore.dll"]
