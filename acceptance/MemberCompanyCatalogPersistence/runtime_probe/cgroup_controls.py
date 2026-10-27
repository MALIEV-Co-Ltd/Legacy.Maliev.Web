"""Fail-closed parsing for a future task-owned delegated cgroup v2."""
import re,stat

def current_group(raw):
    if not isinstance(raw,str) or len(raw)>4096:raise ValueError('bounded cgroup membership required')
    rows=raw.splitlines()
    if len(rows)!=1 or not rows[0].startswith('0::/'):raise ValueError('unified cgroup v2 membership required')
    path=rows[0][3:]
    if path=='/':return path
    parts=path[1:].split('/')
    if any(not re.fullmatch(r'[A-Za-z0-9_.:@-]{1,128}',part) or part in ('.','..') for part in parts):raise ValueError('unsafe cgroup path')
    return path

def populated(raw):
    if not isinstance(raw,str) or len(raw)>1024:raise ValueError('bounded cgroup events required')
    rows={}
    for line in raw.splitlines():
        values=line.split()
        if len(values)!=2 or values[0] in rows or not re.fullmatch(r'[a-z_]+',values[0]) or not values[1].isdigit():raise ValueError('ambiguous cgroup event')
        rows[values[0]]=values[1]
    if rows.get('populated') not in ('0','1'):raise ValueError('exact populated event required')
    return rows['populated']=='1'

def same_directory(before,after):
    fields=('st_dev','st_ino','st_uid','st_mode')
    return all(type(getattr(before,field,None)) is int and getattr(before,field)==getattr(after,field,None) for field in fields) and before.st_dev>=0 and before.st_ino>0 and before.st_uid>=0 and stat.S_ISDIR(before.st_mode)
