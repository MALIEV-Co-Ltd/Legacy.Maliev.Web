const assert=require('node:assert/strict'),fs=require('node:fs'),os=require('node:os'),path=require('node:path'),vm=require('node:vm');
const source=fs.mkdtempSync(path.join(os.tmpdir(),'cnc-region-overlay-'));
try {
 const destination=path.join(source,'occt-import-js/src');fs.mkdirSync(destination,{recursive:true});
 const base=fs.readFileSync(path.join(__dirname,'apply-overlay.cjs'),'utf8');
 const start=base.indexOf("fs.copyFileSync(path.join(__dirname, 'kernel-face.hpp')");
 const end=base.indexOf('child.execFileSync(process.execPath',start);
 assert.ok(start>=0&&end>start);
 vm.runInNewContext(base.slice(start,end),{fs,path,source,__dirname});
 const document=fs.readFileSync(path.join(__dirname,'apply-document-overlay.cjs'),'utf8');
 vm.runInNewContext(document.slice(document.indexOf("for (const file of ['kernel-document.hpp'")),{fs,path,source,__dirname});
 const repair=fs.readFileSync(path.join(__dirname,'apply-repair-overlay.cjs'),'utf8');
 vm.runInNewContext(repair.slice(repair.indexOf("fs.copyFileSync(path.join(__dirname,'kernel-native-interpretation-export.hpp')")),{fs,path,source,__dirname});
 assert.ok(fs.existsSync(path.join(destination,'kernel-region-measures.hpp')),'fresh overlay includes measure helper');
 let checks=0;
 for(const name of fs.readdirSync(destination)){
  assert.deepEqual(fs.readFileSync(path.join(destination,name)),fs.readFileSync(path.join(__dirname,name)));++checks;
  for(const match of fs.readFileSync(path.join(destination,name),'utf8').matchAll(/#include\s+"(kernel-[^"/]+\.hpp)"/g)){
   assert.ok(fs.existsSync(path.join(destination,match[1])),`${name} requires ${match[1]}`);++checks;
  }
 }
 console.log(`PASS: ${checks} combined fresh native overlay header checks`);
} finally {
 assert.equal(path.dirname(source),path.resolve(os.tmpdir()));assert.ok(path.basename(source).startsWith('cnc-region-overlay-'));
 fs.rmSync(source,{recursive:true});
}
