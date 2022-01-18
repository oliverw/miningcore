echo "**********************************************************************************"
echo "Use `build.sh all` to build C++ Libraries"
echo "**********************************************************************************"

echo "========================== STOP SERVICE ==========================================" && \
/etc/init.d/poolservice stop && \
sleep 2 && \
echo "========================== PULL LATEST CODE ======================================" && \
cd ~/miningcore && \
git pull && \
echo "========================== CLEAN OLD BUILD =======================================" && \
cd ~/miningcore/build && \
find . -type f -not -name '*so' -delete && \
echo "Done" && \
echo "========================== BUILD SRC =============================================" && \
cd ~/miningcore/src/ && \
dotnet publish -c Release --framework net5.0  -o ../build  && \

if [ "$1" = "all" ]; then
echo "========================== BUILD LIB =============================================" && \
cd ~/miningcore/src/Native/libmultihash && \
make clean && make && \
yes| cp -rf libmultihash.so ../../../build/ && \
cd ~/miningcore/src/Native/libcryptonight && \
make clean && make && \
yes| cp -rf libcryptonight.so ../../../build/ && \
cd ~/miningcore/src/Native/libcryptonote && \
make clean && make && \
yes| cp -rf libcryptonote.so ../../../build/
fi

echo "========================== START SERVICE =========================================" && \
cd ~/miningcore/build && \
/etc/init.d/poolservice start && \
echo "=========================== START LOG ============================================" && \
tail -f ~/pooldata/logs/core.log