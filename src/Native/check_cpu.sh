set -e
QUERY="$1"

check_mac() {
    case "$1" in
      aes) sysctl -n machdep.cpu.features | grep -i aes >/dev/null;;
      avx) sysctl -n machdep.cpu.features | grep -i avx >/dev/null;;
      avx2) sysctl -n machdep.cpu.features | grep -i avx2 >/dev/null;;
      amd) sysctl -n machdep.cpu.vendor | grep -i amd >/dev/null;;
      amdnew) sysctl -n machdep.cpu.vendor | grep -i amd && test `sysctl -n machdep.cpu.family` -ge 23;;
      intel) sysctl -n machdep.cpu.vendor | grep -i intel >/dev/null;;
      sse2) sysctl -n machdep.cpu.features | grep -i sse2 >/dev/null;;
      sse3) sysctl -n machdep.cpu.features | grep -i sse3 >/dev/null;;
      ssse3) sysctl -n machdep.cpu.features | grep -i ssse3 >/dev/null;;
      avx512f) sysctl -n machdep.cpu.features | grep -i avx512f >/dev/null;;
      xop) sysctl -n machdep.cpu.features | grep -i xop >/dev/null;;
      *) echo "UNRECOGNISED CHECK $QUERY"; exit 1; ;;
    esac
}

check_linux() {
    case "$1" in
      aes) grep -w 'aes' /proc/cpuinfo >/dev/null;;
      avx) grep -w 'avx' /proc/cpuinfo >/dev/null;;
      avx2) grep -w 'avx2' /proc/cpuinfo >/dev/null;;
      amd) grep -i amd /proc/cpuinfo >/dev/null;;
      amdnew) grep -i amd /proc/cpuinfo >/dev/null && test `awk '/cpu family/ && $NF~/^[0-9]*$/ {print $NF}' /proc/cpuinfo | head -n1` -ge 23 >/dev/null;;
      intel) grep -i intel /proc/cpuinfo >/dev/null;;
      sse2) grep -w 'sse2' /proc/cpuinfo >/dev/null;;
      sse3) grep -w 'sse3' /proc/cpuinfo >/dev/null;;
      ssse3) grep -w 'ssse3' /proc/cpuinfo >/dev/null;;
      avx512f) grep -w 'avx512f' /proc/cpuinfo >/dev/null;;
      xop) grep -w 'xop' /proc/cpuinfo >/dev/null;;
      *) echo "UNRECOGNISED CHECK $QUERY"; exit 1; ;;
    esac
}

case "$(uname -a)" in
  Darwin*) check_mac "$QUERY" ;;
  *) check_linux "$QUERY" ;;
esac
