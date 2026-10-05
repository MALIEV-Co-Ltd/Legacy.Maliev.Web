"""Build an actual Web image on hosted CI and execute the immutable producer gate."""
import base64
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import sys
import urllib.request

PRODUCER = 'd583f55473f47f72d33b51060fa5d14e0974daf5'
PRODUCER_BLOB = '8bfb535fdede094dc9efa09d053f0acfea79f48e'
REPOSITORY = 'MALIEV-Co-Ltd/Legacy.Maliev.Web'
DOCKERFILE = 'Legacy.Maliev.Web/Dockerfile'


def require_hosted(environment):
    if environment.get('GITHUB_ACTIONS') != 'true' or environment.get('RUNNER_ENVIRONMENT') != 'github-hosted':
        raise ValueError('Actual image validation requires a GitHub-hosted runner.')
    head = environment.get('WEB_HEAD', '')
    if not re.fullmatch(r'[0-9a-f]{40}', head) or environment.get('GITHUB_REPOSITORY') != REPOSITORY:
        raise ValueError('Exact Web caller identity required.')
    return head


def producer_gate(payload):
    content = base64.b64decode(payload['content'])
    blob = hashlib.sha1(b'blob ' + str(len(content)).encode() + b'\0' + content).hexdigest()
    if payload.get('sha') != PRODUCER_BLOB or blob != PRODUCER_BLOB:
        raise ValueError('Immutable producer content mismatch.')
    import yaml
    workflow = yaml.safe_load(content)
    gates = [step for step in workflow['jobs']['publish']['steps']
             if step.get('name') == 'Verify packaged Web assets and source revision']
    if len(gates) != 1 or gates[0].get('shell') != 'python':
        raise ValueError('Producer gate identity mismatch.')
    return content, gates[0]['run']


def accepted_receipt(payload, head):
    if payload.get('status') != 'accepted' or payload.get('sourceRevision') != head:
        raise ValueError('Actual image/source revision acceptance missing.')
    if payload.get('assetCount') != 22 or payload.get('fontCount', 0) < 10:
        raise ValueError('Actual packaged corpus incomplete.')
    if not re.fullmatch(r'sha256:[0-9a-f]{64}', payload.get('imageId', '')):
        raise ValueError('Immutable actual image identity missing.')
    return payload


def main():
    head = require_hosted(os.environ)
    actual = subprocess.check_output(['git', 'rev-parse', 'HEAD'], text=True).strip()
    if actual != head:
        raise ValueError('Caller checkout does not match exact head.')
    output = Path(os.environ['RUNNER_TEMP']) / 'web-packaged-proof'
    output.mkdir(exist_ok=False)
    receipt = {'webHead': head, 'producer': PRODUCER, 'producerBlob': PRODUCER_BLOB,
               'actualFullWebImageAccepted': False, 'publisherCallerPin': PRODUCER,
               'published': False, 'deployed': False, 'applicationStarted': False,
               'revisionLabelAuthenticityAttestation': False, 'historicalWholeClosure': False}
    image = 'legacy-web-owned-check:' + head
    try:
        url = ('https://api.github.com/repos/MALIEV-Co-Ltd/Legacy.Maliev.Workflows/contents/'
               '.github/workflows/publish-image.yml?ref=' + PRODUCER)
        request = urllib.request.Request(url, headers={'Accept': 'application/vnd.github+json'})
        with urllib.request.urlopen(request, timeout=30) as response:
            payload = json.loads(response.read(100000))
        content, script = producer_gate(payload)
        (output / 'accepted-producer.yml').write_bytes(content)
        gate = output / 'accepted-producer-web-gate.py'
        gate.write_text(script, encoding='utf-8')
        receipt['producerGateSha256'] = hashlib.sha256(script.encode()).hexdigest()
        build = output / 'actual-web-image-build.log'
        with build.open('w', encoding='utf-8') as log:
            result = subprocess.run(['docker', 'build', '--progress=plain',
                                     '--label', 'org.opencontainers.image.revision=' + head,
                                     '--tag', image, '--file', DOCKERFILE, '.'],
                                    stdout=log, stderr=subprocess.STDOUT, timeout=2100)
        receipt['buildExitCode'] = result.returncode
        text = build.read_text(encoding='utf-8')
        receipt['buildWarningDiagnostics'] = len(re.findall(r'\bwarning [A-Z]+\d+\s*:', text))
        receipt['buildErrorDiagnostics'] = len(re.findall(r'\berror [A-Z]+\d+\s*:', text))
        if result.returncode or receipt['buildWarningDiagnostics'] or receipt['buildErrorDiagnostics']:
            raise ValueError('Actual Web image build failed or emitted compiler warnings/errors.')
        environment = dict(os.environ, CALLER_REPOSITORY=REPOSITORY, SOURCE_DOCKERFILE=DOCKERFILE,
                           BUILT_IMAGE=image, EXPECTED_SOURCE_REVISION=head)
        result = subprocess.run([sys.executable, str(gate)], env=environment,
                                capture_output=True, text=True, timeout=300)
        (output / 'actual-web-gate.log').write_text(result.stdout + result.stderr, encoding='utf-8')
        if result.returncode:
            raise ValueError('Accepted producer rejected the actual Web image.')
        gate_receipt = accepted_receipt(json.loads(result.stdout), head)
        receipt.update(actualFullWebImageAccepted=True, gate=gate_receipt)
        print(json.dumps(receipt, sort_keys=True))
    finally:
        (output / 'native-receipt.json').write_text(json.dumps(receipt, indent=2), encoding='utf-8')
        subprocess.run(['docker', 'image', 'rm', image], capture_output=True, timeout=120)


if __name__ == '__main__':
    main()
