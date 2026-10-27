"""Derive an explicitly approximate UTC birth time while retaining exact boot ticks."""
import datetime,re

def estimate(stat_text,ticks,hz):
    if not isinstance(stat_text,str) or len(stat_text)>1024*1024:raise ValueError('bounded boot-time evidence required')
    rows=re.findall(r'^btime ([0-9]+)$',stat_text,re.M)
    if len(rows)!=1 or not isinstance(ticks,str) or not ticks.isdigit() or int(ticks)<=0 or type(hz) is not int or not 1<=hz<=1000000:raise ValueError('exact boot and tick identity required')
    boot=int(rows[0]);seconds,remainder=divmod(int(ticks),hz)
    if boot<=0:raise ValueError('positive boot timestamp required')
    start=datetime.datetime.fromtimestamp(boot+seconds,datetime.timezone.utc)+datetime.timedelta(microseconds=(remainder*1000000)//hz)
    return dict(startUtcApprox=start.isoformat(),bootUtcSeconds=boot,startBootTicks=ticks,clockTicksPerSecond=hz,precision='boot timestamp reported to one second; wall-clock adjustments may affect UTC estimate')
