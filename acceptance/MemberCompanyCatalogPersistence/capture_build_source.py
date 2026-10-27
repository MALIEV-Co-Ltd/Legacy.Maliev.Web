"""Read-only hosted build/package/DLL evidence; never loads or executes an assembly."""
import base64
import hashlib
import json
import os
from pathlib import Path
import re
import struct
import sys
import uuid
import xml.etree.ElementTree as ET
import zipfile
import current_consumer as current
from nuget_signed_verification import verify_signed_package

ROOT=current.ROOT
need=current.need

def inspect_assembly(raw):
    """Bounded ECMA-335 Module/Assembly metadata read; no CLR/package execution."""
    need(0<len(raw)<=16*1024*1024 and raw[:2]==b'MZ','bounded PE assembly')
    def number(at,size):
        need(0<=at<=len(raw)-size,'bounded PE field')
        return int.from_bytes(raw[at:at+size],'little')
    pe=number(60,4);need(raw[pe:pe+4]==b'PE\0\0','PE signature')
    sections=number(pe+6,2);need(0<sections<=96,'bounded PE sections')
    optional=pe+24;magic=number(optional,2);need(magic in (0x10b,0x20b),'supported PE format')
    directories=optional+(96 if magic==0x10b else 112)
    table=optional+number(pe+20,2)
    def offset(rva):
        for index in range(sections):
            at=table+40*index;start=number(at+12,4);size=number(at+16,4)
            if start<=rva<start+size:
                answer=number(at+20,4)+rva-start;need(answer<len(raw),'PE section offset');return answer
        raise ValueError('Current Company: unmapped PE RVA')
    cli=offset(number(directories+14*8,4));metadata=offset(number(cli+8,4))
    need(raw[metadata:metadata+4]==b'BSJB','CLI metadata signature')
    version_size=number(metadata+12,4);need(version_size<=256,'metadata version bound')
    cursor=(metadata+16+version_size+3)&~3;streams=number(cursor+2,2);cursor+=4;need(0<streams<=16,'metadata stream count')
    heaps={}
    for _ in range(streams):
        start,size=number(cursor,4),number(cursor+4,4);name_end=raw.find(b'\0',cursor+8,cursor+40)
        need(name_end>=0,'metadata stream name');name=raw[cursor+8:name_end].decode('ascii');need(name not in heaps and metadata+start+size<=len(raw),'bounded unique metadata stream')
        heaps[name]=(metadata+start,size);cursor=(name_end+4)&~3
    need('#GUID' in heaps and '#Strings' in heaps and ('#~' in heaps or '#-' in heaps),'required CLI heaps')
    tables,tables_size=heaps.get('#~',heaps.get('#-'));sizes=number(tables+6,1);valid=number(tables+8,8);cursor=tables+24
    counts={}
    for index in range(64):
        if valid&(1<<index):counts[index]=number(cursor,4);cursor+=4
    need(counts.get(0)==1 and counts.get(32)==1,'one CLI Module and Assembly')
    string_size=4 if sizes&1 else 2;guid_size=4 if sizes&2 else 2;blob_size=4 if sizes&4 else 2
    def idx(table):return 4 if counts.get(table,0)>=65536 else 2
    def coded(tables,bits):return 4 if max([counts.get(x,0) for x in tables]+[0])>=(1<<(16-bits)) else 2
    type_ref=lambda:coded([2,1,27],2)
    widths={0:2+string_size+3*guid_size,1:coded([0,26,35,1],2)+2*string_size,2:4+2*string_size+type_ref()+idx(4)+idx(6),3:idx(4),4:2+string_size+blob_size,5:idx(6),6:8+string_size+blob_size+idx(8),7:idx(8),8:4+string_size,9:idx(2)+type_ref(),10:coded([2,1,26,6,27],3)+string_size+blob_size,11:2+coded([4,8,23],2)+blob_size,12:coded([6,4,1,2,8,9,10,0,14,23,20,17,26,27,32,35,38,39,40,42,44,43],5)+coded([6,10],3)+blob_size,13:coded([4,8],1)+blob_size,14:2+coded([2,6,32],2)+blob_size,15:6+idx(2),16:4+idx(4),17:blob_size,18:idx(2)+idx(20),19:idx(20),20:2+string_size+type_ref(),21:idx(2)+idx(23),22:idx(23),23:2+string_size+blob_size,24:2+idx(6)+coded([20,23],1),25:idx(2)+2*coded([6,10],1),26:string_size,27:blob_size,28:2+coded([4,6],1)+string_size+idx(26),29:4+idx(4),30:8,31:4}
    guid_index=number(cursor+2+string_size,guid_size);guid_start,guid_length=heaps['#GUID'];need(guid_index>0 and guid_index*16<=guid_length,'Module MVID GUID index')
    mvid=str(uuid.UUID(bytes_le=raw[guid_start+(guid_index-1)*16:guid_start+guid_index*16]));need(uuid.UUID(mvid).int!=0,'nonempty actual MVID')
    assembly=cursor+sum(widths[x]*counts.get(x,0) for x in range(32));need(assembly+16+blob_size+2*string_size<=tables+tables_size,'Assembly table bound')
    version='.'.join(str(number(assembly+4+2*i,2)) for i in range(4));name_index=number(assembly+16+blob_size,string_size)
    string_start,string_length=heaps['#Strings'];need(name_index<string_length,'assembly name index');at=string_start+name_index;end=raw.find(b'\0',at,min(at+257,string_start+string_length));need(end>=at,'bounded assembly name')
    name=raw[at:end].decode('utf-8');need(re.fullmatch('[A-Za-z0-9_.-]+',name),'safe assembly name')
    return dict(assemblyName=name,version=version,mvid=mvid,sha256=hashlib.sha256(raw).hexdigest())

def file_bytes(path,cap=16*1024*1024):
    path=Path(path);return current.read(path.parent,path.name,cap)

def xml_repository(raw,expected_url):
    root=ET.fromstring(raw);row=root.find('{*}metadata/{*}repository')
    need(row is not None and row.get('url')==expected_url and row.get('type')=='git' and re.fullmatch('[0-9a-f]{40}',row.get('commit','')),'immutable official package repository revision')
    return row.get('commit')

def http_output_diagnostic(project,assembly):
    """Report bounded dependency-manifest facts, never claim a DLL was loaded."""
    need(assembly=='Microsoft.Extensions.Http','only the observed missing assembly')
    directory=project+'/bin/Release/net10.0/'
    filename=Path(project).name+'.deps.json'
    if Path(project).name=='Tests':filename='MemberCompanyCatalogPersistence.Tests.deps.json'
    result={'assembly':assembly,'runtimeLoadObserved':False,'appLocalPresent':(ROOT/(directory+assembly+'.dll')).is_file(),'manifest':None,'runtimeConfig':None}
    for field,name in [('manifest',filename),('runtimeConfig',filename.replace('.deps.json','.runtimeconfig.json'))]:
        try:
            raw=current.read(ROOT,directory+name,8*1024*1024);data=current.parse(raw)
            if field=='manifest':
                rows=[]
                for target,packages in data.get('targets',{}).items():
                    if not isinstance(packages,dict):continue
                    for package,detail in packages.items():
                        if not package.startswith('Microsoft.Extensions.Http/') or not isinstance(detail,dict):continue
                        if not re.fullmatch(r'[A-Za-z0-9._/,=+-]{1,100}',target) or not re.fullmatch(r'Microsoft\.Extensions\.Http/[0-9A-Za-z.+-]{1,50}',package):raise ValueError('unsafe diagnostic identity')
                        runtime=detail.get('runtime',{});assets=sorted(k for k in runtime if k.endswith('/Microsoft.Extensions.Http.dll') or k=='Microsoft.Extensions.Http.dll') if isinstance(runtime,dict) else []
                        if len(rows)>=8 or len(assets)>8 or any(not re.fullmatch(r'[A-Za-z0-9._/-]{1,200}',a) for a in assets):raise ValueError('manifest diagnostic bound')
                        rows.append(dict(target=target,package=package,runtimeAssets=assets))
                result[field]=dict(sha256=hashlib.sha256(raw).hexdigest(),httpRows=rows)
            else:
                options=data.get('runtimeOptions',{});frameworks=options.get('frameworks',[]) if isinstance(options,dict) else []
                if isinstance(options,dict) and 'framework' in options:frameworks=frameworks+[options['framework']]
                if not isinstance(frameworks,list) or len(frameworks)>8:raise ValueError('framework diagnostic bound')
                filtered=[f for f in frameworks if isinstance(f,dict) and f.get('name') in ('Microsoft.NETCore.App','Microsoft.AspNetCore.App')]
                if any(not isinstance(f.get('version'),str) or not re.fullmatch(r'[0-9A-Za-z.+-]{1,50}',f['version']) for f in filtered):raise ValueError('unsafe framework version')
                result[field]=dict(sha256=hashlib.sha256(raw).hexdigest(),frameworks=[{'name':f['name'],'version':f['version']} for f in filtered])
        except (OSError,ValueError,KeyError,TypeError):result[field]={'unavailable':True}
    return result

def package_record(assets,name,version,revision,diagnostics=None):
    key=name+'/'+version;libraries=assets['libraries'];need(key in libraries,'exact resolved package version: '+name)
    row=libraries[key];digest=row.get('sha512','').removeprefix('sha512-')
    need(re.fullmatch('[A-Za-z0-9+/]{86}==',digest),'resolved package SHA512')
    folders=list(assets['packageFolders']);need(len(folders)==1,'single actual package cache root')
    package=Path(folders[0])/row['path'];stem=name.lower()+'.'+version
    zipped=file_bytes(package/(stem+'.nupkg'),32*1024*1024)
    if diagnostics is not None:
        diagnostics.update(assetsContentHash=digest,archiveHash=base64.b64encode(hashlib.sha512(zipped).digest()).decode(),cacheArchiveHash=None,metadataContentHash=None,signaturePresent=None)
        try:
            value=file_bytes(package/(stem+'.nupkg.sha512'),1024).decode().strip()
            if re.fullmatch('[A-Za-z0-9+/]{86}==',value):diagnostics['cacheArchiveHash']=value
        except (ValueError,OSError,UnicodeError):pass
        try:
            metadata=current.parse(file_bytes(package/'.nupkg.metadata',65536));value=metadata.get('contentHash') if isinstance(metadata,dict) else None
            if isinstance(value,str) and re.fullmatch('[A-Za-z0-9+/]{86}==',value):diagnostics['metadataContentHash']=value
        except (ValueError,OSError,UnicodeError):pass
        try:
            with zipfile.ZipFile(__import__('io').BytesIO(zipped)) as archive:diagnostics['signaturePresent']=archive.namelist().count('.signature.p7s')==1
        except (ValueError,OSError,zipfile.BadZipFile):pass
    archive_hash=base64.b64encode(hashlib.sha512(zipped).digest()).decode()
    marker=file_bytes(package/(stem+'.nupkg.sha512'),1024).decode().strip();need(marker==archive_hash,'actual archive joins package-cache hash')
    metadata=current.parse(file_bytes(package/'.nupkg.metadata',65536));need(type(metadata) is dict and metadata.get('contentHash')==digest,'actual assets join NuGet content metadata')
    with zipfile.ZipFile(__import__('io').BytesIO(zipped)) as archive:need(archive.namelist().count('.signature.p7s')==1,'one signed package entry required')
    verification={}
    if diagnostics is not None:diagnostics['verification']=verification
    verified_hash=verify_signed_package(package/(stem+'.nupkg'),name+'.'+version,verification)
    need(verification.get('verified') is True and verification.get('terminalVerified') is True and verification.get('pipeEof') is True and verification.get('pidfdAcquired') is True and verification.get('exitCode')==0,'actual signature verifier settled successfully')
    need(verified_hash==digest and verification.get('contentHash')==digest,'verified package content joins assets and metadata')
    need(file_bytes(package/(stem+'.nupkg'),32*1024*1024)==zipped,'verified archive unchanged during verification')
    repository_url={'Microsoft.Extensions.Http.Resilience':'https://github.com/dotnet/extensions','Microsoft.Extensions.Http':'https://github.com/dotnet/dotnet','Polly.Core':'https://github.com/App-vNext/Polly'}[name]
    repository=xml_repository(file_bytes(package/(name.lower()+'.nuspec'),65536),repository_url)
    target=next((value for key,value in assets['targets'].items() if key=='net10.0'),None);need(target is not None and key in target,'exact net10 package target')
    runtime=target[key].get('runtime',{});dll=name+'.dll';members=[path for path in runtime if path.endswith('/'+dll)]
    need(len(members)==1,'single selected package runtime DLL')
    with zipfile.ZipFile(__import__('io').BytesIO(zipped)) as archive:
        nuspecs=[entry for entry in archive.namelist() if entry.lower()==name.lower()+'.nuspec']
        need(archive.namelist().count(members[0])==1 and len(nuspecs)==1,'unique archive assembly and nuspec')
        need(0<archive.getinfo(nuspecs[0]).file_size<=65536,'bounded package nuspec')
        need(archive.read(nuspecs[0])==file_bytes(package/(name.lower()+'.nuspec'),65536),'actual cache nuspec joins verified archive')
        info=archive.getinfo(members[0]);need(0<info.file_size<=16*1024*1024,'bounded package assembly');original=archive.read(info)
    cached=file_bytes(package/members[0]);need(cached==original,'actual cache DLL joins original package')
    return dict(id=name,version=version,revision=repository,sha512=digest),hashlib.sha256(cached).hexdigest()

def capture():
    need(os.environ.get('GITHUB_ACTIONS')=='true','same hosted workflow only; no local SDK/capture role')
    current.authenticate_sources();current.checked_dependency_sources();candidate=current.git(ROOT,'rev-parse','HEAD').decode().strip()
    output=ROOT/'member-company-retained';output.mkdir(exist_ok=True)
    progress=dict(schemaVersion=1,candidateHead=candidate,observedPackages={},failure=None)
    try:
        revisions={}
        for role,name,expected in [('defaults','Legacy.Maliev.ServiceDefaults',current.DEFAULTS),('contracts','Legacy.Maliev.CompatibilityContracts',current.CONTRACTS)]:
            actual=current.git(ROOT/'.dependencies'/name,'rev-parse','HEAD').decode().strip();need(actual==expected,'actual dependency checkout revision');revisions[role]=actual
        projects=[('web-tests','Legacy.Maliev.Web.Tests'),('company-tests','acceptance/MemberCompanyCatalogPersistence/Tests')]
        asset_records=[];loaded=[]
        for role,project in projects:
            raw=current.read(ROOT,project+'/obj/project.assets.json',8*1024*1024);value=current.parse(raw)
            need(value['project']['restore']['projectStyle']=='PackageReference','actual SDK assets shape')
            need(set(value['project']['restore']['sources'])=={'https://api.nuget.org/v3/index.json'},'official actual restore source only')
            asset_records.append(dict(project=role,sha256=hashlib.sha256(raw).hexdigest()));loaded.append(value)
        packages=[];package_hashes={}
        for name,version,revision in current.PACKAGES:
            selected=sorted(k for k in loaded[1]['libraries'] if k.lower().startswith(name.lower()+'/'))
            progress['observedPackages'][name]=selected;need(len(selected)==1,'single actually resolved package')
            actual_version=selected[0].split('/',1)[1]
            records=progress.setdefault('packageDiagnostics',{}).setdefault(name,{})
            record,digest=package_record(loaded[1],name,actual_version,revision,records.setdefault('companyTests',{}));packages.append(record);package_hashes[name]=digest
            other,_=package_record(loaded[0],name,actual_version,revision,records.setdefault('webTests',{}));need(current.same(record,other),'both projects resolve identical runtime package/source')
        generated='.dependencies/Legacy.Maliev.ServiceDefaults/src/Legacy.Maliev.ServiceDefaults/obj/Release/net10.0/Legacy.Maliev.ServiceDefaults.AssemblyInfo.cs'
        raw=current.read(ROOT,generated);matches=re.findall(rb'AssemblyInformationalVersionAttribute\("[^"\r\n]+\+([0-9a-f]{40})"\)',raw)
        need(matches==[current.DEFAULTS.encode()],'actual generated Defaults source revision; never stamp a replacement')
        assemblies=[]
        for role,name,revision in current.ASSEMBLIES:
            if name=='Microsoft.Extensions.Http' and not (ROOT/(projects[1][1]+'/bin/Release/net10.0/'+name+'.dll')).is_file():
                progress['missingAssemblyDiagnostic']=[http_output_diagnostic(project,name) for _,project in projects]
            raw=current.read(ROOT,projects[1][1]+'/bin/Release/net10.0/'+name+'.dll',16*1024*1024);info=inspect_assembly(raw)
            need(info['assemblyName']==name,'actual built assembly name')
            if role!='defaults':need(info['sha256']==package_hashes[name],'built DLL equals original source-bound package DLL')
            else:
                original=current.read(ROOT,'.dependencies/Legacy.Maliev.ServiceDefaults/src/Legacy.Maliev.ServiceDefaults/bin/Release/net10.0/'+name+'.dll',16*1024*1024)
                need(original==raw,'actual built Defaults project output')
            actual_revision=revision if role=='defaults' else next(row['revision'] for row in packages if row['id']==name)
            assemblies.append(dict(role=role,revision=actual_revision,**info))
            other=current.read(ROOT,projects[0][1]+'/bin/Release/net10.0/'+name+'.dll',16*1024*1024);need(other==raw,'both project outputs use same exact runtime DLL')
        logs=[]
        for role,_ in projects:
            raw=current.read(ROOT,'member-company-build-'+role+'.log',8*1024*1024)
            warnings=re.findall(rb'^\s*(\d+) Warning\(s\)\s*$',raw,re.M);errors=re.findall(rb'^\s*(\d+) Error\(s\)\s*$',raw,re.M)
            need(warnings==[b'0'] and errors==[b'0'],'actual build zero warnings/errors summary')
            logs.append(dict(project=role,sha256=hashlib.sha256(raw).hexdigest(),warnings=0,errors=0))
        package_verifications=[dict(id=name,project=project,**progress['packageDiagnostics'][name][project]) for name,_,_ in current.PACKAGES for project in ('companyTests','webTests')]
        evidence=dict(schemaVersion=2,candidateHead=candidate,profileSha256=current.identity(current.read(ROOT,current.PROFILE))['sha256'],sourceChecked=True,
            sourceRevisions=revisions,assemblies=assemblies,packageSources=packages,assets=asset_records,buildLogs=logs,
            generatedDefaultsMetadata=dict(sha256=hashlib.sha256(current.read(ROOT,generated)).hexdigest(),revision=current.DEFAULTS),packageVerifications=package_verifications)
        progress['unqualifiedEvidence']=evidence
        current.validate_build_evidence(evidence,candidate)
        destination=output/'build-source-evidence.json';need(not destination.exists(),'fresh build evidence required');destination.write_text(json.dumps(evidence,indent=2)+'\n')
        print('Actual source/assets/package hashes/generated metadata/DLL evidence retained; runtime journeys not yet qualified')
    except (ValueError,OSError,KeyError,StopIteration,ET.ParseError,zipfile.BadZipFile) as error:
        progress['failure']=type(error).__name__;destination=output/'build-source-refusal.json'
        need(not destination.exists(),'fresh build refusal evidence');destination.write_text(json.dumps(progress,indent=2)+'\n');raise

if __name__=='__main__':capture()
