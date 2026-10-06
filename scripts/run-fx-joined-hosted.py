"""Hosted-only actual Program orchestration. No checkout writes or live provider/IAM calls."""
import argparse
import base64
import hashlib
import json
import os
from pathlib import Path
import re
import secrets
import socket
import subprocess
import sys
import threading
import time
import urllib.error
import urllib.request
import uuid
import xml.etree.ElementTree as ET


def checked(arguments, **kwargs):
    try:
        return subprocess.run(arguments, check=True, text=True, **kwargs)
    except subprocess.CalledProcessError as error:
        raise RuntimeError(Path(arguments[0]).name + ' failed with exit ' + str(error.returncode)) from None


def git(root, *arguments):
    return checked(['git', '-C', str(root), *arguments], capture_output=True).stdout.strip()


def verify_checkout(root, expected):
    if not re.fullmatch('[0-9a-f]{40}', expected or '') or git(root, 'rev-parse', 'HEAD') != expected:
        raise ValueError('Exact reviewed checkout mismatch: ' + str(root))
    if git(root, 'status', '--porcelain'):
        raise ValueError('Clean committed harness/producer checkout required: ' + str(root))


def digest(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def sanitize(text, sensitive):
    for value in sensitive:
        text = text.replace(value, '[REDACTED]')
    return re.sub(r'eyJ[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+', '[JWT REDACTED]', text)


def stop_owned_container(container, run):
    inspected = json.loads(checked(['docker', 'inspect', container], capture_output=True).stdout)[0]
    if inspected['Config']['Labels'].get('maliev.fx.run') != run:
        raise RuntimeError('Refusing cleanup of container not owned by this graph')
    checked(['docker', 'stop', container], capture_output=True)


def verify_trx(path, expected, expect_red=False):
    root = ET.parse(path).getroot()
    counters = next(element for element in root.iter() if element.tag.endswith('}Counters'))
    values = {key: int(value) for key, value in counters.attrib.items()}
    if values['total'] != expected or values['executed'] != expected or values.get('notExecuted', 0):
        raise RuntimeError('Missing, skipped or unexpected native joined test rows')
    if expect_red:
        failures = [element for element in root.iter() if element.tag.endswith('}UnitTestResult')
                    and element.attrib.get('outcome') == 'Failed']
        if len(failures) != expected or any('legacy-catalog.currencies.read' not in ''.join(result.itertext()) for result in failures):
            raise RuntimeError('RED must be the actual missing source-owned grant boundary')
    elif values['passed'] != expected or values.get('failed', 0):
        raise RuntimeError('Native joined assertions failed')
    return values


def port():
    with socket.socket() as listener:
        listener.bind(('127.0.0.1', 0))
        return listener.getsockname()[1]


def env_for_child():
    blocked = ('ServiceClients__', 'Jwt__', 'ConnectionStrings__', 'MALIEV_FX_', 'ASPNETCORE_',
               'DOTNET_ENVIRONMENT', 'OTEL_', 'IAM__', 'QualificationIntrospection__')
    result = {name: value for name, value in os.environ.items() if not name.startswith(blocked)}
    result.update(DOTNET_ENVIRONMENT='Production', ASPNETCORE_ENVIRONMENT='Production',
                  Logging__LogLevel__Default='Warning', Cache__RedisEnabled='false',
                  EmployeeRecovery__Enabled='true', CORS__AllowedOrigins='https://localhost')
    return result


class Children:
    def __init__(self, directory, sensitive):
        self.directory, self.sensitive, self.processes, self.threads = directory, sensitive, [], []

    def start(self, name, arguments, environment, cwd=None):
        process = subprocess.Popen(arguments, env=environment, cwd=cwd, text=True,
                                   stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
        self.processes.append(process)

        def drain():
            with (self.directory / (name + '.log')).open('w', encoding='utf-8') as output:
                pem = False
                for line in process.stdout:
                    if '-----BEGIN' in line:
                        pem = True
                    if pem:
                        if '-----END' in line:
                            pem = False
                        continue
                    output.write(sanitize(line, self.sensitive))
                    output.flush()
        thread = threading.Thread(target=drain, daemon=True)
        thread.start()
        self.threads.append(thread)
        return process

    def close(self):
        for process in reversed(self.processes):
            if process.poll() is None:
                process.terminate()
                try:
                    process.wait(timeout=10)
                except subprocess.TimeoutExpired:
                    process.kill()
                    process.wait(timeout=10)
        for thread in self.threads:
            thread.join(timeout=5)


def readiness(origin, prefix, process):
    deadline = time.monotonic() + 120
    while time.monotonic() < deadline:
        if process.poll() is not None:
            raise RuntimeError(prefix + ' native Program exited before readiness')
        try:
            with urllib.request.urlopen(origin + prefix + '/readiness', timeout=3) as response:
                if response.status == 200:
                    return
        except (urllib.error.URLError, TimeoutError):
            pass
        time.sleep(1)
    raise TimeoutError(prefix + ' native readiness did not become healthy')


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--graph', type=Path, required=True)
    for name in ('web', 'auth', 'catalog', 'web-runtime', 'auth-runtime', 'catalog-runtime'):
        parser.add_argument('--' + name + '-root', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--expect-red', action='store_true')
    args = parser.parse_args()
    # This must precede every SDK, Docker, OpenSSL or HTTP operation.
    if os.environ.get('GITHUB_ACTIONS') != 'true':
        raise RuntimeError('Workflows98 hosted-only lane; local SDK/provider/container starts forbidden')
    graph = json.loads(args.graph.read_text(encoding='utf-8'))
    auth_commit = graph['issuanceCommit']
    if not auth_commit or not graph['issuanceDefaultsCommit']:
        raise ValueError('Root-reviewed exact Auth successor and dependency pin required')
    for name in ('web', 'auth', 'catalog'):
        producer = 'issuance' if name == 'auth' else name
        verify_checkout(getattr(args, name + '_root'), graph[producer + 'Commit'])
        runtime = getattr(args, name + '_runtime_root')
        verify_checkout(runtime / 'Legacy.Maliev.ServiceDefaults', graph[producer + 'DefaultsCommit'])
        verify_checkout(runtime / 'Legacy.Maliev.CompatibilityContracts', graph['contractsCommit'])
    auth_runtime_source = git(args.auth_root, 'show', auth_commit + ':Legacy.Maliev.AuthService.Infrastructure/ServiceAuthentication.cs')
    registered = 'ResolveAuthenticatedClientPermissions' in auth_runtime_source
    if registered == args.expect_red:
        raise ValueError('Auth grant source does not match requested RED/GREEN candidate')
    doc = git(args.auth_root, 'show', auth_commit + ':deploy/README.md')
    lines = [line for line in doc.splitlines() if '`ServiceClients__Clients__legacy-web__SecretSha256`' in line]
    if len(lines) != 1:
        raise ValueError('Unique source-owned 19-grant deployment contract required')
    configured = re.findall(r'`(legacy[\w.-]+)`', lines[0])
    if len(configured) != 19 or len(set(configured)) != 19 or 'legacy-catalog.currencies.read' in configured:
        raise ValueError('Configured source grants changed or fixture currency grant attempted')
    args.output.mkdir(parents=True, exist_ok=False)
    root = args.web_root.resolve()
    project = root / 'tests/fx-joined/FxJoined.Tests.csproj'
    build_jobs = [
        ('auth-host', root / 'tools/fx-auth-host/FxAuthHost.csproj', args.auth_runtime_root, ['-p:FxAuthRoot=' + str(args.auth_root.resolve())]),
        ('catalog-host', root / 'tools/fx-catalog-host/FxCatalogHost.csproj', args.catalog_runtime_root, ['-p:FxCatalogRoot=' + str(args.catalog_root.resolve())]),
        ('web-joined', project, args.web_runtime_root, ['-p:FxWebRoot=' + str(root)]),
    ]
    for name, target, runtime, extra in build_jobs:
        with (args.output / (name + '-build.log')).open('w', encoding='utf-8') as log:
            checked(['dotnet', 'build', str(target), '-c', 'Release', '--nologo', '-warnaserror',
                     '-p:TreatWarningsAsErrors=true', '-p:CI=false', '-p:GITHUB_ACTIONS=false',
                     '-p:UseLocalMalievDependencies=true', '-p:MalievWorkspaceRoot=' + str(runtime.resolve()), *extra],
                    stdout=log, stderr=subprocess.STDOUT)
        text = (args.output / (name + '-build.log')).read_text(encoding='utf-8')
        if not re.search(r'\b0 Warning\(s\)', text) or not re.search(r'\b0 Error\(s\)', text):
            raise RuntimeError('Zero-warning/error build evidence missing: ' + name)
    for name, target, runtime, extra in build_jobs:
        formatting = dict(os.environ, GITHUB_ACTIONS='false', UseLocalMalievDependencies='true',
                          MalievWorkspaceRoot=str(runtime.resolve()), FxAuthRoot=str(args.auth_root.resolve()),
                          FxCatalogRoot=str(args.catalog_root.resolve()), FxWebRoot=str(root))
        with (args.output / (name + '-format.log')).open('w', encoding='utf-8') as log:
            checked(['dotnet', 'format', str(target), '--verify-no-changes', '--no-restore'],
                    env=formatting, stdout=log, stderr=subprocess.STDOUT)
    auth_dll = root / 'tools/fx-auth-host/bin/Release/net10.0/Maliev.FxAuthHost.dll'
    catalog_dll = root / 'tools/fx-catalog-host/bin/Release/net10.0/Maliev.FxCatalogHost.dll'
    binary_receipts = {str(path.relative_to(root)): digest(path) for path in (auth_dll, catalog_dll,
        root / 'tests/fx-joined/bin/Release/net10.0/Legacy.Maliev.Web.dll',
        auth_dll.parent / 'Legacy.Maliev.AuthService.Api.dll', catalog_dll.parent / 'Legacy.Maliev.CatalogService.Api.dll')}
    lanes = {'actor': 'FxGraphLane!=provider&FxGraphLane!=state',
             'provider': 'FxGraphLane=provider', 'state': 'FxGraphLane=state'}
    if args.expect_red:
        lanes = {'actor-red': 'FullyQualifiedName~NormalWebLogin_RealCatalogPermission_ConvertsBothHandlers'}
    receipts = []
    for lane, test_filter in lanes.items():
        output = args.output / lane
        output.mkdir()
        run = str(uuid.uuid4())
        web_secret, other_secret, pg_secret = (secrets.token_hex(32) for _ in range(3))
        keys = checked(['openssl', 'genpkey', '-algorithm', 'RSA', '-pkeyopt', 'rsa_keygen_bits:2048'], capture_output=True).stdout
        public = checked(['openssl', 'pkey', '-pubout'], input=keys, capture_output=True).stdout
        wrong_keys = checked(['openssl', 'genpkey', '-algorithm', 'RSA', '-pkeyopt', 'rsa_keygen_bits:2048'], capture_output=True).stdout
        children = Children(output, [web_secret, other_secret, pg_secret, keys, wrong_keys])
        container = None
        try:
            container = checked(['docker', 'run', '--rm', '-d', '--label', 'maliev.fx.run=' + run,
                                 '-e', 'POSTGRES_PASSWORD=' + pg_secret, '-p', '127.0.0.1::5432', 'postgres:18-alpine'],
                                capture_output=True).stdout.strip()
            inspected = json.loads(checked(['docker', 'inspect', container], capture_output=True).stdout)[0]
            pg_port = int(inspected['NetworkSettings']['Ports']['5432/tcp'][0]['HostPort'])
            for _ in range(60):
                # The image's temporary initialization server accepts Unix sockets only.
                # TCP readiness waits for the final server that native hosts will use.
                result = subprocess.run(['docker', 'exec', container, 'pg_isready', '-h', '127.0.0.1', '-U', 'postgres'], capture_output=True)
                if result.returncode == 0:
                    break
                time.sleep(1)
            else:
                raise RuntimeError('Disposable PostgreSQL not ready')
            names = ('CustomerIdentity', 'EmployeeIdentity', 'RefreshSessions', 'CatalogDbContext', 'CountryDbContext', 'CurrencyDbContext')
            databases = {name: 'fx_' + uuid.UUID(run).hex + '_' + name.lower() for name in names}
            sql = ''.join('CREATE DATABASE "' + database + '";\n' for database in databases.values())
            checked(['docker', 'exec', '-i', container, 'psql', '-h', '127.0.0.1', '-U', 'postgres', '-v', 'ON_ERROR_STOP=1'], input=sql, capture_output=True)
            connection = lambda name: f'Host=127.0.0.1;Port={pg_port};Database={databases[name]};Username=postgres;Password={pg_secret}'
            provider_port, catalog_port = port(), port()
            provider_origin = f'http://127.0.0.1:{provider_port}/'
            fixture_env = env_for_child()
            fixture_env['MALIEV_FX_RUN_ID'] = run
            children.start('provider', [sys.executable, str(root / 'tests/fx-joined/provider_fixture.py'), '--run-id', run,
                                        '--port', str(provider_port)], fixture_env)
            # Auth credentials/grants are generated normal configuration. Currency-read is never supplied.
            auth_environment = env_for_child()
            auth_environment.update(MALIEV_FX_RUN_ID=run,
                MALIEV_FX_AUTH_CONTENT_ROOT=str(args.auth_root.resolve() / 'Legacy.Maliev.AuthService.Api'),
                Jwt__PrivateKeyPem=keys, Jwt__KeyId='disposable-fx', Jwt__Issuer='https://iam.maliev.com',
                Jwt__Audience='maliev-services', Jwt__AccessTokenLifetimeSeconds='900')
            for store in names[:3]:
                auth_environment['ConnectionStrings__' + store] = connection(store)
            for client, secret, permissions in (('legacy-web', web_secret, configured), ('legacy-other', other_secret, ['legacy-contact.messages.create'])):
                stem = 'ServiceClients__Clients__' + client + '__'
                auth_environment[stem + 'SecretSha256'] = hashlib.sha256(secret.encode()).hexdigest()
                for index, permission in enumerate(permissions):
                    auth_environment[stem + 'Permissions__' + str(index)] = permission
            origins = {}
            for variant in ('normal', 'WRONG_AUDIENCE', 'WRONG_SIGNING_KEY', 'EXPIRED'):
                child_env = dict(auth_environment)
                child_port = port()
                child_env['MALIEV_FX_AUTH_PORT'] = str(child_port)
                if variant == 'WRONG_AUDIENCE':
                    child_env['Jwt__Audience'] = 'disposable-wrong-audience'
                if variant == 'WRONG_SIGNING_KEY':
                    child_env['Jwt__PrivateKeyPem'] = wrong_keys
                if variant == 'EXPIRED':
                    child_env['MALIEV_FX_AUTH_CLOCK'] = 'expired'
                native = children.start('auth-' + variant, ['dotnet', str(auth_dll)], child_env, cwd=root)
                origins[variant] = f'http://127.0.0.1:{child_port}/'
                readiness(origins[variant], 'auth', native)
            catalog_env = env_for_child()
            catalog_env.update(MALIEV_FX_RUN_ID=run, MALIEV_FX_PROVIDER_ORIGIN=provider_origin,
                MALIEV_FX_CATALOG_PORT=str(catalog_port),
                MALIEV_FX_CATALOG_CONTENT_ROOT=str(args.catalog_root.resolve() / 'Legacy.Maliev.CatalogService.Api'),
                Jwt__PublicKey=base64.b64encode(public.encode()).decode(), Jwt__Issuer='https://iam.maliev.com', Jwt__Audience='maliev-services')
            for store in names[3:]:
                catalog_env['ConnectionStrings__' + store] = connection(store)
            catalog = children.start('catalog', ['dotnet', str(catalog_dll)], catalog_env, cwd=root)
            catalog_origin = f'http://127.0.0.1:{catalog_port}/'
            readiness(catalog_origin, 'catalog', catalog)
            runtime_manifest = dict(graph, runId=run, configuredWebPermissions=configured, issuer='https://iam.maliev.com', audience='maliev-services')
            manifest = output / 'runtime-graph.json'
            manifest.write_text(json.dumps(runtime_manifest, indent=2), encoding='utf-8')
            environment = env_for_child()
            environment.update(MALIEV_FX_GRAPH_MANIFEST=str(manifest.resolve()), MALIEV_FX_AUTH_ORIGIN=origins['normal'],
                MALIEV_FX_CATALOG_ORIGIN=catalog_origin, MALIEV_FX_PROVIDER_ORIGIN=provider_origin,
                MALIEV_FX_WEB_SECRET=web_secret, MALIEV_FX_OTHER_SECRET=other_secret, MALIEV_FX_PUBLIC_KEY_PEM=public)
            for variant in ('WRONG_AUDIENCE', 'WRONG_SIGNING_KEY', 'EXPIRED'):
                environment['MALIEV_FX_AUTH_' + variant + '_ORIGIN'] = origins[variant]
            test = children.start('joined-test', ['dotnet', 'test', str(project), '-c', 'Release', '--no-build', '--no-restore',
                    '-p:CI=false', '-p:GITHUB_ACTIONS=false', '-p:UseLocalMalievDependencies=true',
                    '-p:MalievWorkspaceRoot=' + str(args.web_runtime_root.resolve()), '-p:FxWebRoot=' + str(root), '--filter', test_filter,
                    '--logger', 'trx;LogFileName=joined.trx', '--results-directory', str(output.resolve())],
                    environment)
            # Invalid upstream bodies can traverse both normal resilience pipelines.
            # This bounds observation only; production request/retry timeouts are unchanged.
            test.wait(timeout=2100 if lane == 'provider' else 1200)
            trx = output / 'joined.trx'
            raw_hash = digest(trx)
            # Preserve the raw hash; sanitize only sensitive fixture values if an assertion exposes them.
            text = trx.read_text(encoding='utf-8')
            sanitized = sanitize(text, (web_secret, other_secret, pg_secret))
            if sanitized != text:
                trx.write_text(sanitized, encoding='utf-8')
            counters = verify_trx(trx, {'actor': 25, 'provider': 28, 'state': 12, 'actor-red': 4}[lane], args.expect_red)
            receipts.append({'lane': lane, 'runId': run, 'exitCode': test.returncode, 'counters': counters,
                             'rawTrxSha256': raw_hash, 'publishedTrxSha256': digest(trx), 'sensitiveRedaction': sanitized != text})
            if test.returncode != 0 and not args.expect_red:
                raise RuntimeError('Actual native joined tests failed: ' + lane)
            if args.expect_red and test.returncode == 0:
                raise RuntimeError('Expected native RED did not fail')
        finally:
            children.close()
            if container:
                stop_owned_container(container, run)
    (args.output / 'native-receipt.json').write_text(json.dumps({'candidateGraph': graph,
        'binarySha256': binary_receipts, 'lanes': receipts, 'expectedRed': args.expect_red,
        'fullProducerAndWebSuitesExecuted': False, 'rawCoverageAcceptance': False,
        'wholeSourceClosure': False}, indent=2), encoding='utf-8')


if __name__ == '__main__':
    main()
