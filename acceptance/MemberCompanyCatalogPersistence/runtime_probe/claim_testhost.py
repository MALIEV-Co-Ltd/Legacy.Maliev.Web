"""Claim one live test host by exact identity and pidfd before acknowledgement."""
import json
from identity_control import validate

class OwnedClaimError(Exception):
    """Acknowledgment failed after ownership was proven; caller must settle this handle."""
    def __init__(self,receipt,fd,reason):
        super().__init__('owned test host acknowledgment failed')
        self.receipt=receipt;self.fd=fd;self.reason=reason

def parse(raw):
    if not isinstance(raw,bytes) or not 0<len(raw)<=16384:raise ValueError('bounded probe receipt required')
    def unique(pairs):
        result={}
        for key,value in pairs:
            if key in result:raise ValueError('duplicate receipt key')
            result[key]=value
        return result
    return json.loads(raw.decode('utf-8','strict'),object_pairs_hook=unique,parse_constant=lambda _: (_ for _ in ()).throw(ValueError('nonfinite receipt')))

def claim(raw,launcher,lookup,descended,pidfd_open,close,acknowledge):
    receipt=parse(raw)
    if type(receipt) is not dict or type(receipt.get('pid')) is not int:raise ValueError('test-host receipt PID required')
    child=receipt['pid'];launcher_pid=launcher['pid']
    validate(receipt,lookup(child),launcher_pid,lambda pid,parent:descended(pid,launcher))
    fd=pidfd_open(child)
    if type(fd) is not int or fd<0:raise ValueError('pidfd acquisition failed')
    try:
        validate(receipt,lookup(child),launcher_pid,lambda pid,parent:descended(pid,launcher))
    except BaseException:
        close(fd)
        raise
    try:acknowledge()
    except Exception as error:raise OwnedClaimError(receipt,fd,type(error).__name__) from error
    return receipt,fd
