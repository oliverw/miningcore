FROM microsoft/dotnet-nightly:2.1-sdk-alpine3.7 as build-env


COPY docker/NuGet.config /tmp/
COPY docker/alpine-build.sh /tmp/
COPY src /tmp/miningcore/src/

# install build-tools and build
RUN apk add --no-cache --virtual .build-deps git cmake build-base \
    openssl-dev pkgconfig boost-dev libsodium-dev && \
    cd /tmp/miningcore/src/MiningCore && \
    sed -i 's|<TargetFramework>.*</TargetFramework>|<TargetFramework>netcoreapp2.1</TargetFramework>|' MiningCore.csproj && \
    cp /tmp/NuGet.config . && \
    cp /tmp/alpine-build.sh . && \
    sh alpine-build.sh /dotnetapp

FROM microsoft/dotnet-nightly:2.1-runtime-deps-alpine3.7

RUN apk add --no-cache boost-system libssl1.0 libuv libsodium icu-libs iputils libcurl && \
    adduser -D -s /bin/sh -u 1000 user &&\
    sed -i -r 's/^user:!:/user:x:/' /etc/shadow && \
    sed -i -r '/^(user|root)/!d' /etc/group && \
    sed -i -r '/^(user|root)/!d' /etc/passwd && \
    find / ! -name 'ping' -xdev -type f -a -perm +4000 -delete && \
    find / -xdev \( -name hexdump -o -name chgrp -o -name chmod -o -name chown -o -name ln -o -name od -o -name strings -o -name su \) -delete && \
    find / -xdev -type l -exec test ! -e {} \; -delete && \
    rm -rf /root && rm -rf /etc/fstab 

WORKDIR /dotnetapp

COPY --chown=user --from=build-env /dotnetapp .
COPY --chown=user --from=build-env /dotnetapp_linux/libuv.so .

USER user

# API
EXPOSE 4000
# Stratum Ports
EXPOSE 3032-3199

ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT false

ENTRYPOINT /dotnetapp/MiningCore -c /config.json

