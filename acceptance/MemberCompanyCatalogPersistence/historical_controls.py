"""Authenticated PR527 fixtures for original controls only; never current admission."""
import contextlib
import ctypes
import importlib.util
import json
import os
from pathlib import Path
import sys
import tempfile
import time
import unittest
import current_consumer as current

def process_identity():
    if os.name=='nt':
        from ctypes import wintypes as w
        kernel=ctypes.WinDLL('kernel32',use_last_error=True);kernel.GetCurrentProcess.restype=w.HANDLE
        kernel.GetProcessTimes.argtypes=[w.HANDLE,*([ctypes.POINTER(w.FILETIME)]*4)];times=[w.FILETIME() for _ in range(4)]
        current.need(kernel.GetProcessTimes(kernel.GetCurrentProcess(),*[ctypes.byref(t) for t in times]),'own historical process identity')
        return dict(pid=os.getpid(),creationFileTime100ns=(times[0].dwHighDateTime<<32)|times[0].dwLowDateTime)
    return dict(pid=os.getpid(),bootId=Path('/proc/sys/kernel/random/boot_id').read_text().strip(),startTicks=Path('/proc/self/stat').read_text().rsplit(') ',1)[1].split()[19])

@contextlib.contextmanager
def historical_suite(root=current.ROOT):
    current.authenticate_bytes(root)
    parent=Path(root).resolve().parent
    with tempfile.TemporaryDirectory(prefix='.company-pr527-controls-',dir=parent) as temporary:
        owner=Path(temporary);(owner/'task-owner.json').write_text(json.dumps(dict(task='Web725-original72-controls',process=process_identity(),created=time.time(),leaseSeconds=180,persistentData=False)))
        target=owner/'source';company=target/'acceptance/MemberCompanyCatalogPersistence';company.mkdir(parents=True)
        prefix='acceptance/MemberCompanyCatalogPersistence/'
        original=current.ARCHIVES[prefix+'retain.py']
        (company/'retain.py').write_bytes(current.read(root,original['path']))
        (company/'test_retain.py').write_bytes(current.read(root,prefix+'test_retain.py'))
        helper=target/'Legacy.Maliev.Web.Tests/BillingProofLifetime.cs';helper.parent.mkdir()
        raw=current.read(root,prefix+'predecessors/helper-pr527.cs.bin')
        current.need(current.identity(raw)['sha256']=='096be55636a60184c452ab0c85a562d0b4b0926e310e95c0a6cf841f00bfccd4','exact historical527 helper')
        helper.write_bytes(raw)
        current.git(target,'init','--initial-branch=historical-controls')
        current.git(target,'config','--local','core.longpaths','true')
        objects=current.git(root,'rev-parse','--git-path','objects').decode().strip()
        objects=(Path(root)/objects).resolve() if not Path(objects).is_absolute() else Path(objects).resolve()
        current.need(objects.is_dir() and '\n' not in str(objects) and '\r' not in str(objects),'actual immutable source object store')
        alternate=target/'.git/objects/info/alternates';alternate.parent.mkdir(parents=True,exist_ok=True);alternate.write_bytes((objects.as_posix()+'\n').encode())
        # The fixture has no invented HEAD. Original exact-SHA git-show controls
        # read actual commit/tree/blob objects through this read-only alternate.
        current.need(current.git(target,'show','4bcf40db748daa1c4fb4db0fba81e8a039f94627:Legacy.Maliev.Web.Tests/BillingProofLifetime.cs')==raw,'actual527 Git object equals fixture')
        previous=sys.modules.get('retain')
        try:
            spec=importlib.util.spec_from_file_location('retain',company/'retain.py');retainer=importlib.util.module_from_spec(spec);sys.modules['retain']=retainer;spec.loader.exec_module(retainer)
            spec=importlib.util.spec_from_file_location('_company_original72',company/'test_retain.py');tests=importlib.util.module_from_spec(spec);spec.loader.exec_module(tests)
            suite=unittest.defaultTestLoader.loadTestsFromModule(tests);current.need(suite.countTestCases()==72,'original72 exact control count')
            yield suite
        finally:
            if previous is None:sys.modules.pop('retain',None)
            else:sys.modules['retain']=previous
