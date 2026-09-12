#!/bin/sh
set -eu
stage="$1"
result="$2"
cd /build
cmp "$stage/kernel-target-query.hpp" /src/occt-import-js/src/kernel-target-query.hpp
cmp "$stage/kernel-target-query-session.hpp" /src/occt-import-js/src/kernel-target-query-session.hpp
em++ -O1 -std=c++11 -fwasm-exceptions -I/src/occt-import-js/src -I"$stage" @CMakeFiles/OcctImportJS.dir/includes_CXX.rsp -c "$stage/test-target-query-native.cpp" -o "$result/test.o"
node -e 'const fs=require("fs"),assert=require("assert/strict"),crypto=require("crypto"); const files=[1,2,3].map(i=>"CMakeFiles/OcctImportJS.dir/objects"+i+".rsp"); const objects=files.flatMap(p=>fs.readFileSync(p,"utf8").trim().split(/\s+/)); const binding="CMakeFiles/OcctImportJS.dir/occt-import-js/src/js-interface.cpp.o"; assert.equal(objects.filter(x=>x===binding).length,1); const retained=objects.filter(x=>x!==binding);fs.writeFileSync(process.argv[1]+"/native-objects.rsp",retained.join("\n")+"\n",{flag:"wx"}); console.log(JSON.stringify({excludedOnly:binding,objects:retained.length,responseHashes:files.map(file=>({file,sha256:crypto.createHash("sha256").update(fs.readFileSync(file)).digest("hex")}))}));' "$result"
em++ -O1 -fwasm-exceptions --bind -sNODERAWFS=1 -sSTACK_SIZE=10MB -sALLOW_MEMORY_GROWTH=1 "$result/test.o" @"$result/native-objects.rsp" -o "$result/test.js"
node "$result/test.js" "$stage/native-common-route-cuboid.step"
