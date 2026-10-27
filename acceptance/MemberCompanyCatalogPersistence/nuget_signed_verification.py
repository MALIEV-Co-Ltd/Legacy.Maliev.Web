"""Verify an actual hosted signed package with one bounded, owned SDK process."""
import base64,hashlib,os,re,selectors,signal,subprocess,sys,time
from pathlib import Path

LIMIT=65536
TIMEOUT=30

def parse_output(raw,exit_code,identity):
    if not isinstance(raw,bytes) or len(raw)>LIMIT or exit_code!=0:raise ValueError('bounded successful verifier required')
    text=raw.decode('utf-8','strict');lines=text.splitlines()
    if any(re.match(r'^[ \t]*(?:error|fatal|warning)(?:[ \t:]|$)',line,re.I) or 'Package signature validation failed.' in line for line in lines):raise ValueError('signature verification error or warning')
    hashes=[m.group(1) for line in lines if (m:=re.fullmatch(r'[ \t]*Content hash: ([A-Za-z0-9+/]{86}==)[ \t]*',line))]
    successes=[line for line in lines if re.match(r'[ \t]*Successfully verified package\b',line)]
    expected=r"[ \t]*Successfully verified package '"+re.escape(identity)+r"'\.[ \t]*"
    if len(hashes)!=1 or len(successes)!=1 or not re.fullmatch(expected,successes[0]):raise ValueError('unique content hash and matching positive signature result required')
    if len(base64.b64decode(hashes[0],validate=True))!=64:raise ValueError('invalid content SHA512')
    return hashes[0]

def proc_identity(pid):
    stat=Path('/proc',str(pid),'stat').read_text();tail=stat.rsplit(') ',1)[1].split()
    ticks=tail[19];boot=Path('/proc/sys/kernel/random/boot_id').read_text().strip();exe=os.path.realpath(os.readlink('/proc/'+str(pid)+'/exe'))
    if not ticks.isdigit() or not re.fullmatch('[0-9a-f-]{36}',boot) or not os.path.isabs(exe):raise ValueError('owned verifier identity unavailable')
    return {'pid':pid,'startTicks':ticks,'bootId':boot,'executable':exe}

def verify_signed_package(package,identity,receipt):
    if sys.platform!='linux' or not hasattr(os,'pidfd_open') or not hasattr(signal,'pidfd_send_signal'):raise ValueError('Linux pidfd ownership required')
    root=Path(os.environ['DOTNET_ROOT']).resolve();binary=(root/'dotnet').resolve();package=Path(package).resolve()
    if not binary.is_absolute() or binary.parent!=root or not package.is_absolute():raise ValueError('absolute SDK and package paths required')
    probe=os.pidfd_open(os.getpid());os.close(probe)
    env=os.environ.copy();env['DOTNET_CLI_UI_LANGUAGE']='en-US';env['LC_ALL']='C'
    selector=selectors.DefaultSelector();process=None;fd=None;identity_record=None;output=bytearray();eof=False;started=time.monotonic()
    try:
        process=subprocess.Popen([str(binary),'nuget','verify',str(package),'--all','--verbosity','normal'],stdout=subprocess.PIPE,stderr=subprocess.STDOUT,env=env)
        receipt.update(identity=identity,pid=process.pid,timeoutSeconds=TIMEOUT,outputLimitBytes=LIMIT,terminalVerified=False,pipeEof=False,pidfdAcquired=False,verified=False)
        identity_record=proc_identity(process.pid);receipt.update(identity_record);fd=os.pidfd_open(process.pid);receipt['pidfdAcquired']=True
        if identity_record['executable']!=str(binary):raise ValueError('owned verifier executable mismatch')
        os.set_blocking(process.stdout.fileno(),False);selector.register(process.stdout,selectors.EVENT_READ)
        while True:
            remaining=TIMEOUT-(time.monotonic()-started)
            if remaining<=0:raise ValueError('owned verifier deadline expired')
            for key,_ in selector.select(min(remaining,0.2)):
                chunk=os.read(key.fd,4096)
                if not chunk:selector.unregister(key.fileobj);eof=True
                else:
                    output.extend(chunk)
                    if len(output)>LIMIT:raise ValueError('owned verifier output bound exceeded')
            if process.poll() is not None and eof:break
        receipt.update(exitCode=process.returncode,pipeEof=True,outputSha256=hashlib.sha256(output).hexdigest(),outputBytes=len(output))
        content_hash=parse_output(bytes(output),process.returncode,identity);receipt['contentHash']=content_hash;receipt['verified']=True
        return content_hash
    finally:
        selector.close()
        try:
            if process is not None and process.poll() is None:
                if fd is None:
                    try:fd=os.pidfd_open(process.pid)
                    except ProcessLookupError:pass
                    except OSError:
                        current=proc_identity(process.pid)
                        if identity_record is None or current!=identity_record:raise ValueError('cannot safely release verifier without exact identity')
                        process.terminate()
                if fd is not None:
                    try:signal.pidfd_send_signal(fd,signal.SIGTERM)
                    except ProcessLookupError:pass
                try:process.wait(timeout=2)
                except subprocess.TimeoutExpired:
                    if fd is not None:
                        try:signal.pidfd_send_signal(fd,signal.SIGKILL)
                        except ProcessLookupError:pass
                    elif identity_record is not None and proc_identity(process.pid)==identity_record:process.kill()
                    else:raise ValueError('cannot safely terminate verifier without exact identity')
                    try:process.wait(timeout=5)
                    except subprocess.TimeoutExpired as error:raise ValueError('owned verifier did not settle') from error
        finally:
            if process is not None and process.stdout is not None:process.stdout.close()
            if process is not None:receipt['terminalVerified']=process.poll() is not None;receipt['pipeEof']=eof;receipt['exitCode']=process.returncode;receipt['elapsedMilliseconds']=int((time.monotonic()-started)*1000)
            if fd is not None:os.close(fd)
