"""Settle only explicitly pinned process handles; never infer descendant cleanup."""
import signal

def settle(fd,is_terminal,send_signal,wait_terminal,receipt=None):
    if type(fd) is not int or fd<0:raise ValueError('owned pidfd required')
    if receipt is None:receipt={}
    receipt.update(fdPinned=True,termSent=False,killSent=False,terminalVerified=False,descendantSettlement='unproved')
    try:
        if is_terminal(fd):
            receipt['terminalVerified']=True
            return receipt
        try:
            send_signal(fd,signal.SIGTERM);receipt['termSent']=True
        except ProcessLookupError:
            pass
        if wait_terminal(fd,2):
            receipt['terminalVerified']=True
            return receipt
        try:
            send_signal(fd,signal.SIGKILL);receipt['killSent']=True
        except ProcessLookupError:
            pass
        if not wait_terminal(fd,5):raise RuntimeError('exact owned process did not settle')
        receipt['terminalVerified']=True
        return receipt
    except Exception as error:
        receipt['cleanupFailure']=type(error).__name__
        raise
