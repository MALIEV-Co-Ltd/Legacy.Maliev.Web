"""Bounded no-follow probe receipt and exclusive acknowledgment operations."""
import os,stat

def flags():
    if not hasattr(os,'O_NOFOLLOW') or not hasattr(os,'O_CLOEXEC'):raise ValueError('no-follow and close-on-exec required')
    return os.O_NOFOLLOW|os.O_CLOEXEC

def _read(path,cap,limit):
    if type(cap) is not int or not 1<=cap<=limit:raise ValueError('bounded receipt cap required')
    fd=os.open(path,os.O_RDONLY|flags())
    try:
        before=os.fstat(fd)
        if not stat.S_ISREG(before.st_mode) or before.st_nlink!=1 or before.st_uid!=os.getuid() or not 0<before.st_size<=cap:raise ValueError('single owned regular receipt required')
        remaining=before.st_size+1;parts=[]
        while remaining:
            chunk=os.read(fd,min(remaining,4096))
            if not chunk:break
            parts.append(chunk);remaining-=len(chunk)
        raw=b''.join(parts);after=os.fstat(fd)
        key=lambda row:(row.st_dev,row.st_ino,row.st_size,row.st_mtime_ns,row.st_uid,row.st_nlink)
        if len(raw)!=before.st_size or key(before)!=key(after):raise ValueError('receipt changed during bounded read')
        return raw
    finally:os.close(fd)

def read_receipt(path,cap=16384):return _read(path,cap,16384)

def read_trx(path,cap=1024*1024):return _read(path,cap,1024*1024)

def acknowledge(path):
    fd=os.open(path,os.O_WRONLY|os.O_CREAT|os.O_EXCL|flags(),0o600)
    try:
        if os.write(fd,b'ok')!=2:raise OSError('short acknowledgment')
    finally:os.close(fd)
