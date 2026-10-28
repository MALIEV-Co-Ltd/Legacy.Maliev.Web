"""Prototype exact cgroup-v2 lease. Never modifies parent controller settings."""
import os,stat,sys,uuid
from pathlib import Path
from cgroup_controls import current_group,populated,same_directory

BASE=Path('/sys/fs/cgroup')
LIMITS={'memory.max':'4294967296','cpu.max':'200000 100000','pids.max':'128'}

def read_fd(fd,cap=4096):
    chunks=[];remaining=cap+1
    while remaining:
        value=os.read(fd,min(remaining,4096))
        if not value:break
        chunks.append(value);remaining-=len(value)
    data=b''.join(chunks)
    if len(data)>cap:raise ValueError('bounded cgroup control required')
    return data.decode('ascii','strict')

def mounted_v2(raw):
    if not isinstance(raw,str) or len(raw)>1024*1024:raise ValueError('bounded mount evidence required')
    found=0
    for line in raw.splitlines():
        left,sep,right=line.partition(' - ')
        if not sep:raise ValueError('invalid mountinfo')
        fields=left.split();kind=right.split()
        if len(fields)<5 or not kind:raise ValueError('invalid mountinfo')
        if fields[4]=='/sys/fs/cgroup' and kind[0]=='cgroup2':found+=1
    if found!=1:raise ValueError('exact unified cgroup mount required')

def bounded_text(path,cap):
    with Path(path).open('r',encoding='utf-8') as stream:value=stream.read(cap+1)
    if len(value)>cap:raise ValueError('bounded proc metadata required')
    return value

class CgroupLease:
    def __init__(self,receipt):
        if sys.platform!='linux' or os.environ.get('GITHUB_ACTIONS')!='true':raise ValueError('hosted Linux only')
        self.receipt=receipt;self.parent_fd=None;self.group_fd=None;self.name=None;self.created=False;self.generation=None;self.membership=None
        self.receipt.update(cgroupCreated=False,cgroupEmptyVerified=False,cgroupRemoved=False,globalControllerMutation=False,crossCgroupEscapeNotIndependentlyExcluded=True)

    def _open_control(self,name,flags):
        if name not in set(LIMITS)|{'cgroup.events','cgroup.procs'}:raise ValueError('unreviewed cgroup control')
        return os.open(name,flags|os.O_NOFOLLOW|os.O_CLOEXEC,dir_fd=self.group_fd)

    def _read(self,name):
        fd=self._open_control(name,os.O_RDONLY)
        try:return read_fd(fd).strip()
        finally:os.close(fd)

    def _same(self):
        if self.group_fd is None or self.parent_fd is None or self.generation is None:return False
        try:entry=os.stat(self.name,dir_fd=self.parent_fd,follow_symlinks=False)
        except FileNotFoundError:return False
        return same_directory(self.generation,os.fstat(self.group_fd)) and same_directory(self.generation,entry)

    def create(self):
        mounted_v2(bounded_text('/proc/self/mountinfo',1024*1024));membership=current_group(bounded_text('/proc/self/cgroup',4096))
        parent=BASE if membership=='/' else BASE/membership.lstrip('/')
        if BASE.is_symlink() or not BASE.is_dir():raise ValueError('unsafe cgroup mount')
        self.parent_fd=os.open(BASE,os.O_RDONLY|os.O_DIRECTORY|os.O_NOFOLLOW|os.O_CLOEXEC)
        try:
            for part in parent.relative_to(BASE).parts:
                next_fd=os.open(part,os.O_RDONLY|os.O_DIRECTORY|os.O_NOFOLLOW|os.O_CLOEXEC,dir_fd=self.parent_fd)
                os.close(self.parent_fd);self.parent_fd=next_fd
        except BaseException:
            os.close(self.parent_fd);self.parent_fd=None
            raise
        self.name='codex-runtime-probe-'+uuid.uuid4().hex
        self.membership=membership.rstrip('/')+'/'+self.name
        try:
            os.mkdir(self.name,0o700,dir_fd=self.parent_fd);self.created=True
            self.group_fd=os.open(self.name,os.O_RDONLY|os.O_DIRECTORY|os.O_NOFOLLOW|os.O_CLOEXEC,dir_fd=self.parent_fd)
            self.generation=os.fstat(self.group_fd)
            if not stat.S_ISDIR(self.generation.st_mode) or self.generation.st_uid!=os.geteuid() or not self._same():raise ValueError('cgroup ownership or generation differs')
            self.receipt.update(cgroupCreated=True,cgroupPath=str(parent/self.name),device=self.generation.st_dev,inode=self.generation.st_ino,ownerUid=self.generation.st_uid,limits=LIMITS.copy())
            for name,value in LIMITS.items():
                fd=self._open_control(name,os.O_WRONLY)
                try:
                    raw=value.encode('ascii')
                    if os.write(fd,raw)!=len(raw):raise OSError('short cgroup control write')
                finally:os.close(fd)
                if self._read(name)!=value:raise ValueError('cgroup resource cap not confirmed')
            if populated(self._read('cgroup.events')) or self._read('cgroup.procs'):raise ValueError('new owned cgroup must be empty')
            return self
        except BaseException:
            self.receipt['cgroupCreateFailure']=True
            try:self.release()
            except Exception as error:self.receipt['cgroupCleanupFailure']=type(error).__name__
            raise

    def attachment_fd(self):
        if not self._same() or populated(self._read('cgroup.events')) or self._read('cgroup.procs'):raise ValueError('fresh empty owned cgroup required')
        return self._open_control('cgroup.procs',os.O_WRONLY)

    def release(self):
        try:
            if not self.created:return
            if not self._same():raise ValueError('cgroup generation unavailable')
            if populated(self._read('cgroup.events')) or self._read('cgroup.procs'):raise ValueError('owned cgroup still populated')
            self.receipt['cgroupEmptyVerified']=True
            os.rmdir(self.name,dir_fd=self.parent_fd)
            try:os.stat(self.name,dir_fd=self.parent_fd,follow_symlinks=False)
            except FileNotFoundError:self.receipt['cgroupRemoved']=True
            else:raise ValueError('cgroup directory remains')
        finally:
            if self.group_fd is not None:os.close(self.group_fd);self.group_fd=None
            if self.parent_fd is not None:os.close(self.parent_fd);self.parent_fd=None
