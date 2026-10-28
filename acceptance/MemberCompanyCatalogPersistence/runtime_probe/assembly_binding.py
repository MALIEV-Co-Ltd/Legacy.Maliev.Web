"""Join observed test-host assembly identities to bounded independently read build outputs."""
from pathlib import Path

WEB='Legacy.Maliev.Web.Tests/bin/Release/net10.0/Legacy.Maliev.Web.Tests.dll'
COMPANY_DIR='acceptance/MemberCompanyCatalogPersistence/Tests/bin/Release/net10.0/'
COMPANY=COMPANY_DIR+'MemberCompanyCatalogPersistence.Tests.dll'
COPIED_WEB=COMPANY_DIR+'Legacy.Maliev.Web.Tests.dll'

def bind(receipt,role,root,read,inspect):
    if role not in ('web','company'):raise ValueError('exact test context required')
    expected={'web':{'testAssembly':WEB,'observerAssembly':WEB},'company':{'testAssembly':COMPANY,'observerAssembly':COPIED_WEB}}[role]
    results={}
    for key,path in expected.items():
        row=receipt.get(key)
        if type(row) is not dict or set(row)!={'name','path','mvid','sha256'} or row['path']!=str(Path(root)/path):raise ValueError('exact test output path required')
        actual=inspect(read(root,path,16*1024*1024))
        if any(row[field]!=actual[actual_field] for field,actual_field in [('name','assemblyName'),('mvid','mvid'),('sha256','sha256')]):raise ValueError('loaded test assembly differs from actual output')
        results[key]=actual
    if role=='company':
        web=inspect(read(root,WEB,16*1024*1024))
        if any(web[key]!=results['observerAssembly'][key] for key in ('assemblyName','mvid','sha256')):raise ValueError('company copy differs from Web test output')
    return True
