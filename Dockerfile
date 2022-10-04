FROM ubuntu:jammy-20220815 as build
RUN apt-get update
RUN DEBIAN_FRONTEND=noninteractive apt-get install -y \
	    sudo \
            wget \
            apt-transport-https \
            dotnet-sdk-6.0 \
            git \
            cmake \
            build-essential \
            libssl-dev \
            pkg-config \
            libboost-all-dev \
            libsodium-dev \
            libzmq3-dev \
            libzmq5


WORKDIR /app
RUN mkdir /app/build
COPY . .

WORKDIR /app/src/Miningcore
ENV BUILDIR=/app/build/
RUN dotnet publish -c Release --framework net6.0 -o /app/build/
RUN mkdir /usr/local/miningcore
RUN cp -rf /app/build/* /usr/local/miningcore/

#
# Copy build artifacts into a new image
#
FROM ubuntu:jammy-20220815
RUN mkdir /usr/local/miningcore
WORKDIR /usr/local/miningcore/
COPY --from=build /usr/local/miningcore/ /usr/local/miningcore/
RUN apt update
RUN DEBIAN_FRONTEND=noninteractive apt-get install -y \
   apt-transport-https curl dotnet6 libssl-dev pkg-config libboost-all-dev libsodium-dev libzmq5 libzmq3-dev 

EXPOSE 4000-4090

ENTRYPOINT ["dotnet","/usr/local/miningcore/Miningcore.dll", "-c","/app/config.json"]


