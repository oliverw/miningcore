@echo off
cd src\Miningcore
dotnet publish -c Release --framework net6.0 -o ../../build
