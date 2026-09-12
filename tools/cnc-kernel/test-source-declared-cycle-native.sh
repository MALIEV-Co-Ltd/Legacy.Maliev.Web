#!/bin/sh
set -eu
cd /build
stage="$1"
result="$2"
em++ -O1 -std=c++11 -fwasm-exceptions -I/src/occt-import-js/src @CMakeFiles/OcctImportJS.dir/includes_CXX.rsp -c "$stage/test-source-declared-cycle-native.cpp" -o "$result/test.o"
em++ -O1 -fwasm-exceptions --bind -sNODERAWFS=1 -sSTACK_SIZE=10MB -sALLOW_MEMORY_GROWTH=1 "$result/test.o" @CMakeFiles/OcctImportJS.dir/objects1.rsp @CMakeFiles/OcctImportJS.dir/objects2.rsp @CMakeFiles/OcctImportJS.dir/objects3.rsp -o "$result/test.js"
node "$result/test.js" "$stage/analytic-outside-chamfer.step"
