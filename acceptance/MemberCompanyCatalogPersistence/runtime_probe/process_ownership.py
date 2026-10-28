"""Linux identity and bounded ancestry checks, independent of process execution."""
import os,re,uuid
from pathlib import Path

def identity(pid):
    if type(pid) is not int or pid<=0:raise ValueError('positive PID required')
    stat=Path('/proc',str(pid),'stat').read_text();prefix,separator,tail=stat.rpartition(') ')
    if not separator or not prefix.startswith(str(pid)+' ('):raise ValueError('process stat identity')
    values=tail.split()
    if len(values)<20 or not values[1].isdigit() or int(values[1])<=0 or not values[19].isdigit() or int(values[19])<=0:raise ValueError('process birth and parent required')
    boot=Path('/proc/sys/kernel/random/boot_id').read_text().strip()
    if str(uuid.UUID(boot))!=boot:raise ValueError('canonical boot identity required')
    executable=os.path.realpath(os.readlink('/proc/'+str(pid)+'/exe'))
    if not os.path.isabs(executable):raise ValueError('absolute executable required')
    return dict(pid=pid,startTicks=values[19],bootId=boot,executable=executable,parentId=values[1])

def descended(child_pid,launcher,lookup=identity,maximum=16):
    if type(maximum) is not int or not 1<=maximum<=16:raise ValueError('bounded ancestry required')
    if type(launcher) is not dict or type(launcher.get('pid')) is not int:raise ValueError('owned launcher identity required')
    seen=set();current=child_pid
    for _ in range(maximum):
        if current in seen or current<=0:return False
        seen.add(current)
        try:row=lookup(current)
        except (OSError,ValueError,KeyError):return False
        if row.get('pid')!=current or row.get('bootId')!=launcher.get('bootId'):return False
        if current==launcher['pid']:return row==launcher
        parent=row.get('parentId')
        if not isinstance(parent,str) or not parent.isdigit():return False
        current=int(parent)
    return False
