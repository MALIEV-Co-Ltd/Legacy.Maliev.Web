#!/bin/sh
set -eu
cd /build
mkdir -p /tmp/thread-transform-native-results
em++ -O1 -std=c++11 -fwasm-exceptions -I/src/occt-import-js/src @CMakeFiles/OcctImportJS.dir/includes_CXX.rsp -c /tmp/thread-transform-native-source/test-repair-thread-transform-native.cpp -o /tmp/thread-transform-native-results/test.o
em++ -O1 -fwasm-exceptions --bind -sSTACK_SIZE=10MB -sALLOW_MEMORY_GROWTH=1 /tmp/thread-transform-native-results/test.o @CMakeFiles/OcctImportJS.dir/objects1.rsp @CMakeFiles/OcctImportJS.dir/objects2.rsp @CMakeFiles/OcctImportJS.dir/objects3.rsp -o /tmp/thread-transform-native-results/test.js
node /tmp/thread-transform-native-results/test.js
