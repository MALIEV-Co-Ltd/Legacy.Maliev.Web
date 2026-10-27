"""Require fresh physical memory without altering system limits or caches."""
import re

MIN_AVAILABLE_KIB=4*1024*1024

def available_kib(raw):
    if not isinstance(raw,str) or len(raw)>65536:raise ValueError('bounded memory evidence required')
    rows=re.findall(r'^MemAvailable:\s+(\d+) kB\s*$',raw,re.M)
    if len(rows)!=1:raise ValueError('one physical memory observation required')
    value=int(rows[0])
    if value<MIN_AVAILABLE_KIB:raise ValueError('physical memory admission refused')
    return value
