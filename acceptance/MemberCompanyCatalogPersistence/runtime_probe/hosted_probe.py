"""Prototype hosted-only no-backend test-host probe. Not yet integrated or qualified."""
import hashlib,json,os,selectors,select,signal,stat,subprocess,sys,tempfile,time
from pathlib import Path
from claim_testhost import claim,OwnedClaimError
from owned_receipt_file import acknowledge,read_receipt,read_trx
from process_ownership import identity,descended
from process_settlement import settle
from cgroup_lease import CgroupLease,bounded_text
from cgroup_controls import current_group
from memory_admission import available_kib
from trx_control import verify as verify_trx
from assembly_binding import bind as bind_assemblies
from birth_time import estimate as estimate_birth

TIMEOUT=60
LIMIT=65536
DOTNET='/usr/share/dotnet/dotnet'
PROJECTS={'company':'acceptance/MemberCompanyCatalogPersistence/Tests/MemberCompanyCatalogPersistence.Tests.csproj','web':'Legacy.Maliev.Web.Tests/Legacy.Maliev.Web.Tests.csproj'}
FILTERS={'company':'FullyQualifiedName~Legacy.Maliev.Web.Tests.CompanyRuntimeAssemblyProbe.ObserveHttpAssemblyWithoutBackend','web':'FullyQualifiedName~Legacy.Maliev.Web.Tests.RuntimeAssemblyProbe.ObserveHttpAssemblyWithoutBackend'}
CLASSES={'company':'Legacy.Maliev.Web.Tests.CompanyRuntimeAssemblyProbe','web':'Legacy.Maliev.Web.Tests.RuntimeAssemblyProbe'}
ASSEMBLIES={'company':'acceptance/MemberCompanyCatalogPersistence/Tests/bin/Release/net10.0/MemberCompanyCatalogPersistence.Tests.dll','web':'Legacy.Maliev.Web.Tests/bin/Release/net10.0/Legacy.Maliev.Web.Tests.dll'}

def verify_output_assemblies(receipt,role,root):
    source_parent=str(Path(__file__).resolve().parent.parent)
    sys.path.insert(0,source_parent)
    import current_consumer
    from capture_build_source import inspect_assembly
    return bind_assemblies(receipt,role,root,current_consumer.read,inspect_assembly)

def birth_evidence(ticks):return estimate_birth(bounded_text('/proc/stat',1024*1024),ticks,os.sysconf('SC_CLK_TCK'))

def enter_owned_group(fd):
    if os.write(fd,b'0')!=1:os._exit(126)

def terminal_events(events,fd):
    # Linux poll: POLLIN=1, POLLERR=8, POLLHUP=16, POLLNVAL=32.
    if not events:return False
    if len(events)!=1 or events[0][0]!=fd or events[0][1] & (8|32) or events[0][1] & ~(1|16):raise ValueError('invalid pidfd poll evidence')
    return bool(events[0][1] & (1|16))

def terminal(fd):
    poller=select.poll();poller.register(fd,select.POLLIN|select.POLLHUP)
    return terminal_events(poller.poll(0),fd)

def wait_terminal(fd,seconds):
    poller=select.poll();poller.register(fd,select.POLLIN|select.POLLHUP)
    return terminal_events(poller.poll(int(seconds*1000)),fd)

def cleanup_private(private,host_pinned,descendants_settled):
    if not descendants_settled:raise ValueError('descendant custody not settled')
    allowed={'observation.json','observation.json.ack','observation.json.pending','probe.trx'}
    items=list(private.iterdir())
    if len(items)>4 or any(p.name not in allowed for p in items):raise ValueError('unexpected private probe files')
    if items and not host_pinned:raise ValueError('unclaimed host file custody')
    for path in items:
        row=path.lstat()
        if not stat.S_ISREG(row.st_mode) or row.st_nlink!=1 or row.st_uid!=os.getuid():raise ValueError('unsafe private probe file')
    for path in items:path.unlink()
    private.rmdir()
    if private.exists():raise ValueError('private probe directory remains')

def create_private(root,artifact_dir,role):
    parent=root/'.member-company-runtime-probe';private=None;parent_identity=None;private_identity=None
    try:
        parent.mkdir(mode=0o700,exist_ok=False);parent_identity=parent.lstat()
        if not stat.S_ISDIR(parent_identity.st_mode) or parent_identity.st_uid!=os.getuid() or parent_identity.st_mode&0o077:raise ValueError('private probe parent permissions')
        private=Path(tempfile.mkdtemp(prefix='owned-runtime-probe-',dir=parent));private_identity=private.lstat()
        if not stat.S_ISDIR(private_identity.st_mode) or private_identity.st_uid!=os.getuid() or private_identity.st_mode&0o077:raise ValueError('private probe directory permissions')
        return parent,private
    except BaseException as error:
        recovery=dict(role=role,failure=type(error).__name__,privatePath=str(private) if private else None,parentPath=str(parent),cleanup='unproved',backendAllocated=False)
        try:
            if private is not None:
                now=private.lstat()
                if private_identity is None or (now.st_dev,now.st_ino)!=(private_identity.st_dev,private_identity.st_ino) or not stat.S_ISDIR(now.st_mode) or now.st_uid!=os.getuid():raise ValueError('private setup identity unproved')
                private.rmdir()
            if parent_identity is not None:
                now=parent.lstat()
                if (now.st_dev,now.st_ino)!=(parent_identity.st_dev,parent_identity.st_ino) or not stat.S_ISDIR(now.st_mode) or now.st_uid!=os.getuid():raise ValueError('private parent identity changed')
                parent.rmdir();recovery['cleanup']='verified'
        except Exception as cleanup_error:recovery['cleanupFailure']=type(cleanup_error).__name__
        with (artifact_dir/(role+'-runtime-probe-setup-refusal.json')).open('x',encoding='utf-8') as stream:json.dump(recovery,stream,indent=2)
        raise

def run_one(role,root,artifact_dir):
    if sys.platform!='linux' or os.environ.get('GITHUB_ACTIONS')!='true':raise ValueError('hosted Linux only')
    if role not in PROJECTS or not hasattr(os,'pidfd_open') or not hasattr(signal,'pidfd_send_signal'):raise ValueError('exact role and pidfd required')
    if os.getuid()<=0 or os.getuid()!=os.geteuid():raise ValueError('matching unprivileged process identity required')
    if any(os.environ.get(key) for key in ('DOTNET_STARTUP_HOOKS','DOTNET_ADDITIONAL_DEPS','DOTNET_SHARED_STORE','LD_PRELOAD','LD_LIBRARY_PATH')):raise ValueError('unreviewed runtime injection refused')
    root=Path(root).resolve();artifact_dir=Path(artifact_dir)
    if artifact_dir.is_symlink():raise ValueError('artifact directory link refused')
    artifact_dir=artifact_dir.resolve()
    workspace=os.environ.get('GITHUB_WORKSPACE')
    if not workspace or root!=Path(workspace).resolve() or not root.is_dir() or not artifact_dir.is_dir() or artifact_dir.parent!=root or artifact_dir.name!='member-company-retained':raise ValueError('owned workspace artifact directory required')
    free_kib=available_kib(bounded_text('/proc/meminfo',65536))
    probe=os.pidfd_open(os.getpid());os.close(probe)
    scratch_parent,private=create_private(root,artifact_dir,role)
    observation=private/'observation.json';ack=Path(str(observation)+'.ack')
    summary=dict(role=role,timeoutSeconds=TIMEOUT,outputLimitBytes=LIMIT,physicalAvailableKiB=free_kib,privatePath=str(private),backendAllocated=False,descendantSettlement='unproved',launcher=None,testHost=None,cgroup={},privateCleanup='unproved',success=False)
    process=None;launcher_fd=None;test_fd=None;selector=None;lease=None;spawn_attempted=False;output=bytearray();eof=False;started=time.monotonic()
    try:
        lease=CgroupLease(summary['cgroup']);lease.create();attachment=lease.attachment_fd()
        environment=os.environ.copy();environment['MALIEV_RUNTIME_PROBE_OUTPUT']=str(observation)
        command=[DOTNET,'test',PROJECTS[role],'-c','Release','--no-build','--no-restore','-p:CI=false','-p:VSTestCollect=','-p:RunSettingsFilePath=','--filter',FILTERS[role],'--logger','trx;LogFileName=probe.trx','--results-directory',str(private)]
        try:
            spawn_attempted=True
            process=subprocess.Popen(command,cwd=root,env=environment,stdout=subprocess.PIPE,stderr=subprocess.STDOUT,start_new_session=True,preexec_fn=lambda:enter_owned_group(attachment),pass_fds=(attachment,))
            summary['launcher']={'pid':process.pid,'identityCaptured':False,'terminalVerified':False}
            launcher_fd=os.pidfd_open(process.pid)
        finally:os.close(attachment)
        launcher=identity(process.pid);summary['launcher'].update(launcher,identityCaptured=True,birth=birth_evidence(launcher['startTicks']))
        if launcher['executable']!=DOTNET:raise ValueError('unexpected launcher executable')
        if current_group(bounded_text('/proc/'+str(process.pid)+'/cgroup',4096))!=lease.membership:raise ValueError('launcher outside owned cgroup')
        os.set_blocking(process.stdout.fileno(),False);selector=selectors.DefaultSelector();selector.register(process.stdout,selectors.EVENT_READ)
        while True:
            remaining=TIMEOUT-(time.monotonic()-started)
            if remaining<=0:raise TimeoutError('owned runtime probe deadline')
            if test_fd is None:
                try:raw=read_receipt(observation)
                except FileNotFoundError:raw=None
                if raw is not None:
                    def acknowledge_owned():
                        candidate=json.loads(raw.decode('utf-8'))
                        if current_group(bounded_text('/proc/'+str(candidate['pid'])+'/cgroup',4096))!=lease.membership:raise ValueError('test host outside owned cgroup')
                        verify_output_assemblies(candidate,role,root)
                        acknowledge(ack)
                    try:observed,test_fd=claim(raw,launcher,identity,descended,os.pidfd_open,os.close,acknowledge_owned)
                    except OwnedClaimError as error:
                        test_fd=error.fd;summary['testHost']=dict(error.receipt,pidfdAcquired=True,terminalVerified=False,acknowledgmentFailure=error.reason)
                        summary['testHost']['birth']=birth_evidence(error.receipt['startTicks'])
                        raise
                    summary['testHost']=dict(observed,pidfdAcquired=True,terminalVerified=False,birth=birth_evidence(observed['startTicks']))
            for key,_ in selector.select(min(remaining,0.1)):
                chunk=os.read(key.fd,4096)
                if not chunk:selector.unregister(key.fileobj);eof=True
                else:
                    output.extend(chunk)
                    if len(output)>LIMIT:raise ValueError('owned runtime probe output bound')
            if process.poll() is not None and eof:break
        if test_fd is None or process.returncode!=0 or not wait_terminal(test_fd,5):raise ValueError('test host and launcher must finish successfully')
        summary['trx']=verify_trx(read_trx(private/'probe.trx'),CLASSES[role],str(root/ASSEMBLIES[role]))
        summary['testHost']['terminalVerified']=True;summary['launcher']['terminalVerified']=terminal(launcher_fd)
        if not summary['launcher']['terminalVerified']:raise ValueError('launcher terminal identity unproved')
        summary.update(outputBytes=len(output),outputSha256=hashlib.sha256(output).hexdigest(),pipeEof=True,exitCode=process.returncode,success=True)
    except Exception as error:
        summary['failure']=type(error).__name__
        raise
    finally:
        if selector is not None:selector.close()
        for label,fd in [('testHost',test_fd),('launcher',launcher_fd)]:
            if fd is not None:
                cleanup={};summary[label]['cleanup']=cleanup
                try:settle(fd,terminal,signal.pidfd_send_signal,wait_terminal,cleanup)
                except Exception as error:summary['success']=False;summary[label]['cleanupFailure']=type(error).__name__
                finally:os.close(fd)
        if process is not None:
            if process.stdout is not None:process.stdout.close()
            summary['launcher']['exitCode']=process.poll()
            if summary['launcher']['exitCode'] is None:summary['success']=False
        if lease is not None:
            try:
                if lease.parent_fd is not None or lease.group_fd is not None:lease.release()
                if summary['cgroup'].get('cgroupEmptyVerified') is True and summary['cgroup'].get('cgroupRemoved') is True:summary['descendantSettlement']='owned-cgroup-empty'
            except Exception as error:summary['cgroupCleanupFailure']=type(error).__name__;summary['success']=False
        if not spawn_attempted:summary['descendantSettlement']='not-started'
        summary['pipeEof']=eof;summary['outputBytes']=len(output);summary['outputSha256']=hashlib.sha256(output).hexdigest();summary['elapsedMilliseconds']=int((time.monotonic()-started)*1000)
        # Write recovery evidence before deleting any owned scratch file.
        destination=artifact_dir/(role+'-runtime-probe.json')
        with destination.open('x',encoding='utf-8') as stream:json.dump(summary,stream,indent=2)
        original=destination.lstat()
        try:
            cleanup_private(private,test_fd is not None and summary.get('testHost',{}).get('cleanup',{}).get('terminalVerified') is True,summary['descendantSettlement'] in ('owned-cgroup-empty','not-started'))
            scratch_parent.rmdir()
            if scratch_parent.exists():raise ValueError('private probe parent remains')
            summary['privateCleanup']='verified'
        except Exception as error:
            summary['privateCleanupFailure']=type(error).__name__;summary['success']=False
        temporary=artifact_dir/(role+'-runtime-probe-final.json.pending')
        with temporary.open('x',encoding='utf-8') as stream:json.dump(summary,stream,indent=2)
        current=destination.lstat()
        if (current.st_dev,current.st_ino)!=(original.st_dev,original.st_ino):raise ValueError('owned recovery receipt replaced')
        os.replace(temporary,destination)
    if not summary['success']:raise ValueError('runtime probe did not qualify')
    return summary
