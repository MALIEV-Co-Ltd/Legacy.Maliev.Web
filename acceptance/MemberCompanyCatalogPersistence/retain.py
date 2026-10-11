"""Strict, fail-closed outcome-only retention for the isolated synthetic company lane."""
import datetime
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import xml.etree.ElementTree as ET

NS = {'t': 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}
PREFIX = 'Legacy.Maliev.Web.Tests.MemberCompanyCatalogPersistenceTests.CompanyUpdate_RealAdapter_NormalSave_ApiReadbackAndReload'
# PR527 changed billing attempt archives, not OwnershipReceipt() used by this lane
# (which constructs the owner with proofCulture:null). Qualify only that exact successor.
HELPER_REVISION = '4bcf40db748daa1c4fb4db0fba81e8a039f94627'
HELPER_SHA256 = '096be55636a60184c452ab0c85a562d0b4b0926e310e95c0a6cf841f00bfccd4'

def require(value, message):
    if not value:
        raise ValueError(message)

def original_run(value):
    # Guid serializes as D; original backend labels and names use Guid.ToString("N").
    # Normalize only this exact comparison, never rewrite the original receipt.
    require(isinstance(value,str) and re.fullmatch(r'(?:[0-9a-f]{32}|[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12})',value), 'Canonical original run UUID required')
    return value.replace('-','')

def helper_source(path):
    data = path.read_bytes()
    require(hashlib.sha256(data).hexdigest() == HELPER_SHA256, 'Exact PR527 lifetime receipt successor source required')
    return HELPER_SHA256

def rows(path):
    root = ET.parse(path).getroot()
    results = root.findall('.//t:UnitTestResult', NS)
    require(len(results) == 2, 'Exactly two company native rows required')
    expected = {PREFIX + '(culture: "en", width: 1280)', PREFIX + '(culture: "th", width: 375)'}
    require({r.get('testName') for r in results} == expected, 'Exact company case names required')
    require(all(r.get('outcome') == 'Passed' for r in results), 'Both actual native company cases must pass')
    counters = root.find('.//t:Counters', NS)
    require(counters is not None and all(counters.get(k) == v for k,v in {'total':'2','executed':'2','passed':'2','failed':'0','error':'0','timeout':'0','aborted':'0','inconclusive':'0','passedButRunAborted':'0','notRunnable':'0','notExecuted':'0','disconnected':'0','warning':'0','completed':'0','inProgress':'0','pending':'0'}.items()), 'Strict native counters required')
    for field in ['testId','executionId']:
        values = [r.get(field,'') for r in results]
        require(len(set(values)) == 2 and all(re.fullmatch('[0-9a-fA-F-]{36}',v) for v in values), 'Nonempty unique native row identity required')
    return [{'testName': r.get('testName'), 'outcome': 'Passed', 'testId': r.get('testId'), 'executionId': r.get('executionId')} for r in results]

def receipt(value, culture, candidate):
    fields = {'surface','culture','width','candidateHead','companyId','selectedCompany','selectedTaxId','catalogStatus','saveStatus','readbackStatus','companyReadbackStatus','reloadVerified','contactsAndAddressIdentitiesPreserved','manualRegistrarPreserved','deniedLookupStatus','deniedWriteStatus','deniedWriteUnchanged','unavailableStatus','rateLimitedStatus','manualFallbackEditable','syntheticUpstreamOnly','observations','fixtureNullEmailRejectedStatus','fixturePreparedStatus','preparedFixtureEmailPreserved','runtimeRetryPolicy','browserRequests'}
    require(set(value) == fields, 'Only exact bounded receipt fields retained')
    expected = {'surface':'member-company-update','culture':culture,'width':1280 if culture == 'en' else 375,'candidateHead':candidate,'selectedTaxId':'0123456789012','catalogStatus':200,'saveStatus':302,'readbackStatus':200,'companyReadbackStatus':200,'deniedLookupStatus':403,'deniedWriteStatus':403,'unavailableStatus':503,'rateLimitedStatus':429,'fixtureNullEmailRejectedStatus':400,'fixturePreparedStatus':204}
    require(all(value.get(k) == v for k,v in expected.items()), 'Business receipt contract mismatch')
    require(isinstance(value['companyId'], int) and value['companyId'] > 0, 'Original company ID required')
    require(value['selectedCompany'] == ('Synthetic Company Limited' if culture == 'en' else '\u0e1a\u0e23\u0e34\u0e29\u0e31\u0e17\u0e2a\u0e31\u0e07\u0e40\u0e04\u0e23\u0e32\u0e30\u0e2b\u0e4c \u0e08\u0e33\u0e01\u0e31\u0e14'), 'Synthetic selected name required')
    for flag in ['reloadVerified','contactsAndAddressIdentitiesPreserved','manualRegistrarPreserved','deniedWriteUnchanged','manualFallbackEditable','syntheticUpstreamOnly','preparedFixtureEmailPreserved']:
        require(value[flag] is True, 'Actual proof flag missing: '+flag)
    policy = value['runtimeRetryPolicy']
    require(isinstance(policy,dict) and set(policy) == {'clientName','runtimeDefaultsRevision','handlerCount','pipelineOptions','pipelineOrderFromPinnedSource','runtimePackageSourceRevision','confirmedFromActualHandlerChain'}, 'Exact runtime retry evidence required')
    require(policy['clientName'] == 'catalog' and policy['runtimeDefaultsRevision'] == '3c790ba6414b2a539f24aabb6948549ffd81a86b', 'Original named client/runtime source required')
    require(type(policy['handlerCount']) is int and policy['handlerCount'] == 2 and policy['runtimePackageSourceRevision'] == '02107c65bab30aad9e35b5133ed643eaa77bccd8', 'Two actual retry handlers and original package source required')
    require(policy['pipelineOrderFromPinnedSource'] == ['-standard','catalog-standard'], 'Explicit pinned-source pipeline order required')
    pipeline_options = policy['pipelineOptions']
    require(isinstance(pipeline_options,list) and len(pipeline_options) == 2, 'Two separate actual pipeline option entries required')
    for entry,key in zip(pipeline_options,['-standard','catalog-standard']):
        require(isinstance(entry,dict) and set(entry) == {'optionsKey','maxRetryAttempts'} and entry['optionsKey'] == key and type(entry['maxRetryAttempts']) is int and entry['maxRetryAttempts'] == 3, 'Exact separately read original max-three options required')
    require(policy['confirmedFromActualHandlerChain'] is True, 'Unconfirmed source-only retry policy cannot qualify')
    phases = [('Synthetic',200),('Unavailable',503),('RateLimited',429)]
    requests = value['browserRequests']
    require(isinstance(requests,list) and len(requests) == 3, 'Exactly three browser request phases required')
    for request,(prefix,status) in zip(requests,phases):
        require(isinstance(request,dict) and set(request) == {'query','language','count'}, 'Exact browser request metadata required')
        require(request['query'] == prefix+' '+culture and request['language'] == culture and type(request['count']) is int and request['count'] == 1, 'Exactly one actual browser request per distinct phase required')
    maximum = (1+pipeline_options[0]['maxRetryAttempts'])*(1+pipeline_options[1]['maxRetryAttempts'])
    observed = value['observations']
    require(isinstance(observed,list) and 3 <= len(observed) <= 1+2*maximum, 'Bounded actual physical provider attempts required')
    counts = [0,0,0]; phase = 0
    for row in observed:
        require(isinstance(row,dict) and set(row) == {'logicalUri','physicalLoopback','method','typeSearch','query','language','status'}, 'Unexpected upstream metadata')
        require(row['logicalUri'] == 'https://data.creden.co/sapi/search/get_suggestion' and row['physicalLoopback'] is True and row['method'] == 'POST' and row['typeSearch'] == 'prefix' and row['language'] == culture and type(row['status']) is int, 'Original request/body/origin/culture required')
        matches = lambda index: row['query'] == phases[index][0]+' '+culture and row['status'] == phases[index][1]
        if not matches(phase):
            require(phase < 2 and counts[phase] > 0 and matches(phase+1), 'Exactly three contiguous success/unavailable/rate-limited phases required')
            phase += 1
        counts[phase] += 1
    require(phase == 2 and counts[0] == 1 and all(1 <= count <= maximum for count in counts[1:]), 'One physical success and each first actual failure required within runtime-derived bound')
    return value

def timestamp(value):
    require(isinstance(value,str), 'UTC timestamp required')
    try:
        parsed = datetime.datetime.fromisoformat(value.replace('Z','+00:00'))
    except ValueError:
        raise ValueError('Valid UTC timestamp required')
    require(parsed.utcoffset() == datetime.timedelta(0), 'UTC timestamp offset required')
    return parsed

def cleanup(value):
    require(set(value) == {'owner','run','expiresUtc','released','readerFailureRejected','retained','expired','expiryFailure','policy','hosts','children','backends'}, 'Exact owner receipt schema required')
    require(value.get('owner') == 'web-billing-proof' and value.get('released') is True and value.get('retained') is False and value.get('readerFailureRejected') is False and value.get('expired') is False and value.get('expiryFailure') is None, 'Original joined owner did not release')
    run = original_run(value.get('run'))
    owner_expiry = timestamp(value['expiresUtc'])
    require(value['policy'] == 'Same-owner 1800-second expiry task closes admission, frontends and actual case work before dependent cleanup; unresolved ownership is quarantined, never accepted as release.', 'Original lifetime policy required')
    require(len(value.get('hosts',[])) == 11 and all(set(row) == {'startupComplete','disposal'} and row['startupComplete'] is True and row['disposal'] == 'RanToCompletion' for row in value['hosts']), 'Exactly all eleven admitted provider/Web/browser/HTTP hosts must settle')
    child_keys = {'dispatched','identityCaptured','exitVerified','noChildVerified','readersClosed','released','process'}
    require(len(value.get('children',[])) == 3 and all(set(row) == child_keys and all(row.get(flag) is True for flag in ['dispatched','identityCaptured','exitVerified','readersClosed','released']) and row['noChildVerified'] is False for row in value['children']), 'Exactly three dispatched original seed/Customer/Catalog children must settle')
    seen_children = set()
    for child in value['children']:
        process = child['process']
        require(isinstance(process,dict) and set(process) == {'pid','birth','executable','startRefused','ExpiresUtc','stdout','stderr','stdoutClosed','stderrClosed'}, 'Exact original child process schema required')
        require(type(process['pid']) is int and process['pid'] > 0, 'Original child PID required')
        executable = process['executable']
        require(isinstance(executable,str) and len(executable) <= 4096 and re.fullmatch(r'/(?:[A-Za-z0-9_.+-]+/)*dotnet', executable) and '..' not in executable.split('/'), 'Original absolute dotnet executable required')
        identity = (process['pid'],timestamp(process['birth']),executable)
        require(identity not in seen_children, 'Distinct original child generation required')
        seen_children.add(identity)
        require(process['startRefused'] is False and process['stdout'] == 'RanToCompletion' and process['stderr'] == 'RanToCompletion' and process['stdoutClosed'] is True and process['stderrClosed'] is True, 'Original child reader settlement required')
        require(timestamp(process['birth']) < timestamp(process['ExpiresUtc']) and timestamp(process['birth']) < owner_expiry, 'Original child generation and expiry required')
    require(len(value.get('backends',[])) == 1, 'One original PG journey backend required')
    backend = value['backends'][0]
    backend_keys = {'backend','localEndpoint','databaseSynthetic','name','database','owner','run','expiresUtc','daemon','originalId','baselineAbsent','startDispatched','startupSettled','captureVerified','absenceVerified','sdkReleased','original'}
    require(set(backend) == backend_keys, 'No unapproved public backend field permitted')
    original = backend.get('original') or {}; cid = backend.get('originalId')
    original_keys = {'id','createdUtc','imageId','name','owner','run','expiresUtc','configuredImage','envelopeValid','initEnabled','envelopeSignatureSha256','memoryBytes','swapBytes','nanoCpus','tmpfs','loopbackPorts','persistentData'}
    require(set(original) == original_keys, 'No unapproved original envelope field permitted')
    require(re.fullmatch('[0-9a-f]{64}', cid or '') and original['id'] == cid, 'Original PG ID join required')
    require(all(backend.get(flag) is True for flag in ['databaseSynthetic','baselineAbsent','startDispatched','startupSettled','captureVerified','absenceVerified','sdkReleased']), 'Actual PG identity/start/removal required')
    require(backend['backend'] == 'postgres' and backend['owner'] == value['owner'] and backend['run'] == run and backend['name'] == 'billing-proof-'+run+'-postgres' and backend['localEndpoint'] == 'unix:///var/run/docker.sock' and re.fullmatch('profile_contract_[0-9a-f]{32}', backend['database']), 'Original owner/run/name/database/local daemon join required')
    require(isinstance(backend['daemon'],str) and re.fullmatch('[A-Za-z0-9:_-]{1,256}',backend['daemon']), 'Bounded original daemon identity required')
    require(original['name'] == '/'+backend['name'] and original['owner'] == value['owner'] and original['run'] == run and original['expiresUtc'] == backend['expiresUtc'], 'Original resource ownership and expiry join required')
    require(re.fullmatch('sha256:[0-9a-f]{64}',original['imageId']) and re.fullmatch('[0-9a-f]{64}',original['envelopeSignatureSha256']), 'Original image/signature hashes required')
    require(timestamp(original['createdUtc']) < owner_expiry < timestamp(original['expiresUtc']), 'Original allocation and distinct owner/backend expiry ordering required')
    require(all(timestamp(original['createdUtc']) < timestamp(child['process']['birth']) for child in value['children']), 'Original backend birth must precede dependent child generations')
    require(all(original.get(k) == v for k,v in {'configuredImage':'postgres:18-alpine','envelopeValid':True,'initEnabled':True,'memoryBytes':536870912,'swapBytes':536870912,'nanoCpus':1000000000,'tmpfs':'/var/lib/postgresql:rw,size=268435456','loopbackPorts':True,'persistentData':False}.items()), 'Original PG resource envelope required')
    return {key:value[key] for key in ['owner','run','expiresUtc','released','retained','readerFailureRejected','expired','expiryFailure','hosts']} | {'children':[ {key:row[key] for key in ['dispatched','identityCaptured','exitVerified','readersClosed','released']} for row in value['children']], 'backends':[{key:backend[key] for key in sorted(backend_keys)}]}

def main():
    candidate = os.environ.get('MALIEV_COMPANY_CANDIDATE_HEAD','')
    require(re.fullmatch('[0-9a-f]{40}',candidate), 'Exact candidate required')
    require(subprocess.check_output(['git','rev-parse','HEAD'],text=True).strip() == candidate, 'Executed checkout must equal candidate')
    helper_digest = helper_source(Path('Legacy.Maliev.Web.Tests/BillingProofLifetime.cs'))
    source = Path('acceptance/MemberCompanyCatalogPersistence/Tests/bin/Release/net10.0/TestResults/member-company')
    output = Path('member-company-retained'); output.mkdir(exist_ok=True)
    native = list(Path('member-company-test-results').glob('*.trx'))
    require(len(native) == 1, 'One native company TRX required')
    native_rows = rows(native[0])
    seen_ids = set(); seen_runs = set(); raw_cleanup_hashes = {}
    for culture in ['en','th']:
        case = receipt(json.loads((source/(culture+'.json')).read_text()), culture, candidate)
        raw_cleanup = (source/(culture+'-cleanup.json')).read_bytes()
        raw_cleanup_hashes[culture] = hashlib.sha256(raw_cleanup).hexdigest()
        released = cleanup(json.loads(raw_cleanup))
        cid = released['backends'][0]['originalId']
        normalized_run = original_run(released['run'])
        require(cid not in seen_ids and normalized_run not in seen_runs, 'Distinct actual journey ownership required')
        seen_ids.add(cid); seen_runs.add(normalized_run)
        for name, data in [(culture+'.json',case),(culture+'-cleanup.json',released)]:
            (output/name).write_text(json.dumps(data,indent=2,ensure_ascii=True)+'\n')
        png = (source/(culture+'.png')).read_bytes()
        require(png.startswith(b'\x89PNG\r\n\x1a\n') and len(png) < 4000000, 'Bounded actual synthetic screenshot required')
        (output/(culture+'.png')).write_bytes(png)
    watchdog = json.loads(Path('billing-watchdog-results/watchdog.json').read_text())
    require(all(watchdog.get(k) == v for k,v in {'image':'postgres:18-alpine','identityPolicyVerified':True,'initEnabled':True,'forcedKillVerified':True,'absenceVerified':True,'termObserved':True,'exitCode':137}.items()), 'Actual watchdog gate required')
    require(re.fullmatch('[0-9a-f]{64}',watchdog.get('containerId','')) and 3 <= watchdog.get('elapsedSeconds',0) <= 25, 'Original watchdog ID/timing required')
    (output/'watchdog.json').write_text(json.dumps(watchdog,indent=2)+'\n')
    (output/'native-outcomes.json').write_text(json.dumps({'candidateHead':candidate,'rawTrxSha256':hashlib.sha256(native[0].read_bytes()).hexdigest(),'nativeRows':native_rows,'passed':2,'failed':0,'skipped':0,'assertionTextRetained':False},indent=2)+'\n')
    controls = json.loads(Path('member-company-control-results/transport-controls.json').read_text())
    require(set(controls) == {'nativeTransportControls','rejectedCases','rejectedPhysicalAttempts','validPhysicalAttempts','realNetworkAllocated','validLoopbackUriVerified'}, 'Strict executable transport controls schema required')
    require(controls['rejectedCases'] == ['physical-origin-0','physical-origin-1','physical-origin-2','physical-origin-3','logical-http','logical-other-host','logical-other-path','logical-query','method','body','authorization'] and all(controls.get(k) == v for k,v in {'nativeTransportControls':True,'rejectedPhysicalAttempts':0,'validPhysicalAttempts':1,'realNetworkAllocated':False,'validLoopbackUriVerified':True}.items()), 'Actual same-handler transport guard controls required')
    (output/'transport-controls.json').write_text(json.dumps(controls,indent=2)+'\n')
    sources = {str(p):hashlib.sha256(p.read_bytes()).hexdigest() for p in Path('acceptance/MemberCompanyCatalogPersistence').rglob('*') if p.is_file() and not any(x in p.parts for x in ['bin','obj','__pycache__'])}
    sources['.github/workflows/member-company-catalog-persistence.yml'] = hashlib.sha256(Path('.github/workflows/member-company-catalog-persistence.yml').read_bytes()).hexdigest()
    sources['Legacy.Maliev.Web.Tests/BillingProofLifetime.cs'] = helper_digest
    # Public immutable source revisions; these are repository identities, never credential material.
    service_git_revisions = [
        {'repository':'MALIEV-Co-Ltd/Legacy.Maliev.CatalogService',
         'revision':'3f426723743570a6c20d2c014499445abb0774e1'},
        {'repository':'MALIEV-Co-Ltd/Legacy.Maliev.CustomerService',
         'revision':'dc090542c02d675f54c3be59e33654dc24ecca9e'},
        {'repository':'MALIEV-Co-Ltd/Legacy.Maliev.AuthService',
         'revision':'51afbbd6e2829382a3431338abedccf339de33b1'},
        {'repository':'MALIEV-Co-Ltd/Legacy.Maliev.ServiceDefaults',
         'revision':'7edcd961024868513fd5f373cab3dcb261197f77'},
    ]
    (output/'source-provenance.json').write_text(json.dumps({'candidateHead':candidate,'sourceSha256':sources,'lifetimeHelperRevision':HELPER_REVISION,'rawCleanupSha256':raw_cleanup_hashes,'publicServiceGitRevisions':service_git_revisions,'sharedAuthorityLifecycleQualified':False,'wholeAppHostQualified':False,'genuineIamEnrollmentQualified':False,'liveProviderQualified':False},indent=2)+'\n')
    print('Accepted exactly two isolated company UPDATE native cases, original PG cleanup and bounded synthetic upstream observations')

if __name__ == '__main__':
    main()
