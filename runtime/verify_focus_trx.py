"""Exact execution-membership reader; retains red evidence and never equates names with executions."""
import argparse,collections,hashlib,json,pathlib,re,uuid,xml.etree.ElementTree as ET
NS={'t':'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}
COUNTERS={'total','executed','passed','failed','error','timeout','aborted','inconclusive','passedButRunAborted','notRunnable','notExecuted','disconnected','warning','completed','inProgress','pending'}
def verify(root,expected):
    receipt={'schemaVersion':1,'accepted':False,'groups':{},'errors':[]}
    all_ids=[]
    for group,names in expected.items():
        files=list((root/group).glob('*.trx'))
        if len(files)!=1:
            receipt['errors'].append(group+': expected exactly one TRX');continue
        p=files[0]
        try:
            t=ET.parse(p);summary=t.find('.//t:ResultSummary',NS);c=summary.find('t:Counters',NS).attrib;rows=t.findall('.//t:UnitTestResult',NS)
            identities=[x.attrib['executionId'] for x in rows];actual=[x.attrib['testName'] for x in rows]
            all_ids.extend(identities)
            if set(c)!=COUNTERS or any(re.fullmatch(r'0|[1-9][0-9]*',x) is None for x in c.values()):raise ValueError('Noncanonical counter schema')
            for identity in identities:
                if str(uuid.UUID(identity))!=identity:raise ValueError('Noncanonical lowercase execution GUID')
            failed=[{'case':x.attrib,'output':ET.tostring(x.find('t:Output',NS),encoding='unicode') if x.find('t:Output',NS) is not None else None} for x in rows if x.attrib['outcome']!='Passed']
            entry={'path':str(p),'sha256':hashlib.sha256(p.read_bytes()).hexdigest(),'counters':c,'resultOutcome':summary.attrib['outcome'],'rows':len(rows),'uniqueExecutionIds':len(set(identities)),'failures':failed}
            receipt['groups'][group]=entry
            if collections.Counter(names)!=collections.Counter(actual):receipt['errors'].append(group+': execution membership differs')
            if len(identities)!=len(set(identities)):receipt['errors'].append(group+': duplicate execution ID')
            if int(c['total'])!=len(names) or int(c['executed'])!=len(names) or len(rows)!=len(names):receipt['errors'].append(group+': executed/total/raw rows differ')
            if sum(x.attrib['outcome']=='Passed' for x in rows)!=int(c['passed']) or sum(x.attrib['outcome']=='Failed' for x in rows)!=int(c['failed']):receipt['errors'].append(group+': outcome counters differ')
            if summary.attrib['outcome']!='Completed' or failed or int(c['passed'])!=len(names):receipt['errors'].append(group+': native group not fully passed')
            if any(int(c[k]) for k in COUNTERS-{'total','executed','passed'}):receipt['errors'].append(group+': nonpassing/unfinished counters')
        except (KeyError,ValueError,ET.ParseError,AttributeError) as e:receipt['errors'].append(group+': invalid TRX '+type(e).__name__)
    found=list(root.rglob('*.trx'))
    if len(found)!=len(expected):receipt['errors'].append('Unexpected extra or missing TRX file')
    if len(all_ids)!=len(set(all_ids)):receipt['errors'].append('Execution ID reused across groups')
    receipt['accepted']=not receipt['errors'];return receipt
def main():
    p=argparse.ArgumentParser();p.add_argument('--results-root',type=pathlib.Path,required=True);p.add_argument('--expected',type=pathlib.Path,required=True);p.add_argument('--receipt',type=pathlib.Path,required=True);a=p.parse_args()
    expected=json.loads(a.expected.read_text(encoding='utf-8'));r=verify(a.results_root,expected);a.receipt.parent.mkdir(parents=True,exist_ok=True);a.receipt.write_text(json.dumps(r,indent=2),encoding='utf-8');print(json.dumps({'accepted':r['accepted'],'errors':r['errors']}));return 0 if r['accepted'] else 1
if __name__=='__main__':raise SystemExit(main())
