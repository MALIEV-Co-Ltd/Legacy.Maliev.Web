"""Run both bounded no-backend observations serially after authenticated builds."""
import json,os,sys
from pathlib import Path
from hosted_probe import run_one

def main():
    if sys.platform!='linux' or os.environ.get('GITHUB_ACTIONS')!='true':raise ValueError('hosted Linux only')
    workspace=os.environ.get('GITHUB_WORKSPACE')
    if not workspace:raise ValueError('hosted workspace required')
    root=Path(workspace).resolve();output=root/'member-company-retained'
    output.mkdir(mode=0o700,exist_ok=False)
    for role in ('web','company'):
        try:
            result=run_one(role,root,output)
            if result.get('success') is not True:raise ValueError('runtime probe not qualified')
        except Exception as error:
            refusal=output/(role+'-runtime-probe-admission-refusal.json')
            with refusal.open('x',encoding='utf-8') as stream:json.dump(dict(role=role,failure=type(error).__name__,actualRuntimeAccepted=False,backendAllocated=False),stream,indent=2)
            raise

if __name__=='__main__':main()
