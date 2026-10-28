"""Verify exactly one passed diagnostic test and its actual assembly join."""
import hashlib,uuid,xml.etree.ElementTree as ET

NS='{http://microsoft.com/schemas/VisualStudio/TeamTest/2010}'
NONPASS=('failed','error','timeout','aborted','inconclusive','passedButRunAborted','notRunnable','notExecuted','disconnected','warning','completed','inProgress','pending')

def guid(value):
    try:
        parsed=uuid.UUID(value)
        if str(parsed)!=value or parsed.int==0:raise ValueError('canonical test identity required')
        return value
    except (ValueError,TypeError,AttributeError) as error:raise ValueError('canonical test identity required') from error

def verify(raw,expected_class,expected_assembly):
    if not isinstance(raw,bytes) or not 0<len(raw)<=1024*1024 or b'<!DOCTYPE' in raw.upper() or b'<!ENTITY' in raw.upper():raise ValueError('bounded safe test result required')
    doc=ET.fromstring(raw)
    if doc.tag!=NS+'TestRun':raise ValueError('TRX namespace required')
    results=doc.findall(NS+'Results/'+NS+'UnitTestResult');tests=doc.findall(NS+'TestDefinitions/'+NS+'UnitTest');entries=doc.findall(NS+'TestEntries/'+NS+'TestEntry');counts=doc.findall(NS+'ResultSummary/'+NS+'Counters')
    if any(len(rows)!=1 for rows in (results,tests,entries,counts)):raise ValueError('one complete test join required')
    result,test,entry,counter=results[0],tests[0],entries[0],counts[0]
    if any(counter.get(name)!=value for name,value in {'total':'1','executed':'1','passed':'1',**{key:'0' for key in NONPASS}}.items()):raise ValueError('exact passed counters required')
    method=test.find(NS+'TestMethod');execution=test.find(NS+'Execution');full=expected_class+'.ObserveHttpAssemblyWithoutBackend'
    if method is None or execution is None or method.get('className')!=expected_class or method.get('name')!='ObserveHttpAssemblyWithoutBackend' or method.get('codeBase')!=expected_assembly or test.get('name')!=full or result.get('testName')!=full or result.get('outcome')!='Passed':raise ValueError('exact target host test required')
    test_id=guid(test.get('id'));execution_id=guid(execution.get('id'))
    if guid(result.get('testId'))!=test_id or guid(entry.get('testId'))!=test_id or guid(result.get('executionId'))!=execution_id or guid(entry.get('executionId'))!=execution_id:raise ValueError('test result identity mismatch')
    return dict(test=full,testId=test_id,executionId=execution_id,sha256=hashlib.sha256(raw).hexdigest(),passed=1,failed=0,skipped=0)
