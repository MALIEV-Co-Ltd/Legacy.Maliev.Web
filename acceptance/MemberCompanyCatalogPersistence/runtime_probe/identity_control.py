"""Validate a synthetic test-host receipt; never execute an SDK or accept runtime parity."""
import re,uuid

FIELDS={'pid','startTicks','bootId','executable','parentId','assemblyName','assemblyVersion','informationalVersion','mvid','assemblyPath','sha256','testAssembly','observerAssembly','backendAllocated'}

def validate(receipt,actual,launcher_pid,ancestor_check):
    if type(receipt) is not dict or set(receipt)!=FIELDS:raise ValueError('exact runtime probe schema required')
    if type(receipt['pid']) is not int or receipt['pid']<=0 or receipt['pid']==launcher_pid:raise ValueError('test-host PID required')
    if not isinstance(receipt['startTicks'],str) or not receipt['startTicks'].isdigit() or int(receipt['startTicks'])<=0:raise ValueError('test-host birth identity required')
    if not isinstance(receipt['parentId'],str) or not receipt['parentId'].isdigit() or int(receipt['parentId'])<=0:raise ValueError('test-host parent required')
    try:
        if str(uuid.UUID(receipt['bootId']))!=receipt['bootId'] or uuid.UUID(receipt['bootId']).int==0 or str(uuid.UUID(receipt['mvid']))!=receipt['mvid'] or uuid.UUID(receipt['mvid']).int==0:raise ValueError('canonical identity required')
    except (ValueError,TypeError,AttributeError) as exc:raise ValueError('canonical identity required') from exc
    if receipt['assemblyName']!='Microsoft.Extensions.Http' or receipt['backendAllocated'] is not False:raise ValueError('exact no-backend assembly required')
    if not isinstance(receipt['assemblyVersion'],str) or not re.fullmatch(r'[0-9]+(?:\.[0-9]+){3}',receipt['assemblyVersion']):raise ValueError('assembly version required')
    if not isinstance(receipt['informationalVersion'],str) or not re.fullmatch(r'[0-9A-Za-z.+-]{1,128}',receipt['informationalVersion']):raise ValueError('safe source version required')
    if not isinstance(receipt['sha256'],str) or not re.fullmatch(r'[0-9a-f]{64}',receipt['sha256']):raise ValueError('assembly SHA256 required')
    if not isinstance(receipt['assemblyPath'],str) or not re.fullmatch(r'/usr/share/dotnet/shared/Microsoft\.AspNetCore\.App/[0-9A-Za-z.+-]+/Microsoft\.Extensions\.Http\.dll',receipt['assemblyPath']):raise ValueError('exact framework assembly path required')
    if not isinstance(receipt['executable'],str) or receipt['executable']!='/usr/share/dotnet/dotnet':raise ValueError('exact hosted dotnet executable required')
    for key in ('testAssembly','observerAssembly'):
        row=receipt[key]
        if type(row) is not dict or set(row)!={'name','path','mvid','sha256'} or row['name'] not in ('Legacy.Maliev.Web.Tests','MemberCompanyCatalogPersistence.Tests'):raise ValueError('exact test assembly receipt required')
        if not isinstance(row['path'],str) or not row['path'].startswith('/') or len(row['path'])>4096 or '\\' in row['path'] or '\x00' in row['path'] or not isinstance(row['sha256'],str) or not re.fullmatch('[0-9a-f]{64}',row['sha256']):raise ValueError('bounded test assembly identity required')
        try:
            if str(uuid.UUID(row['mvid']))!=row['mvid'] or uuid.UUID(row['mvid']).int==0:raise ValueError('canonical test assembly MVID required')
        except (ValueError,TypeError,AttributeError) as exc:raise ValueError('canonical test assembly MVID required') from exc
    if actual!={'pid':receipt['pid'],'startTicks':receipt['startTicks'],'bootId':receipt['bootId'],'executable':receipt['executable'],'parentId':receipt['parentId']}:raise ValueError('live process identity differs')
    if not ancestor_check(receipt['pid'],launcher_pid):raise ValueError('test host is not descended from owned launcher')
    return dict(receipt)
