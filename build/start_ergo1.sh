#!/bin/sh
/usr/share/dotnet/dotnet /opt/miningcore/build/Miningcore.dll -c /opt/miningcore/build/ergo1.json 2>&1 >> /opt/miningcore/logs/console.log
