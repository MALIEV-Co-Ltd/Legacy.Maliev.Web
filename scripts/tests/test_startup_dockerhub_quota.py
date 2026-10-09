import email.message
import importlib.util
import io
import json
from pathlib import Path
import subprocess
import tempfile
import threading
from types import SimpleNamespace
import unittest
import urllib.error
from unittest.mock import Mock

SOURCE = Path(__file__).resolve().parents[1] / "probe-startup-dockerhub-quota.py"
spec = importlib.util.spec_from_file_location("quota", SOURCE)
q = importlib.util.module_from_spec(spec)
spec.loader.exec_module(q)


def headers(**values):
    result = email.message.Message()
    for key, value in values.items():
        result[key.replace("_", "-")] = value
    return result


class Response:
    def __init__(self, status=200, body=b'', fields=None):
        self.status = status
        self.body = body
        self.headers = fields or headers()
        self.reads = []

    def read(self, size):
        self.reads.append(size)
        return self.body[:size]

    def __enter__(self):
        return self

    def __exit__(self, *args):
        pass


def probe(remaining="99;w=21600", limit="100;w=21600", status=200, extra=None):
    auth = Response(body=b'{"token":"PRIVATE-TOKEN"}')
    fields = headers(RateLimit_Limit=limit, RateLimit_Remaining=remaining,
                     Date="Fri, 09 Oct 2026 21:00:00 GMT")
    for key, value in (extra or {}).items():
        fields[key] = value
    head = Response(status=status, fields=fields)
    opener = Mock()
    opener.open.side_effect = [auth, head]
    return q.probe_http(opener), opener, auth, head


class QuotaTests(unittest.TestCase):
    def test_original_get_and_head_only_positive_without_private_headers(self):
        result, opener, auth, head = probe(extra={"docker-ratelimit-source":"PRIVATE-IP", "X-Private":"PRIVATE"})
        self.assertTrue(result['eligible'])
        self.assertEqual((result['limit'], result['remaining'], result['windowSeconds']), (100, 99, 21600))
        requests = [call.args[0] for call in opener.open.call_args_list]
        self.assertEqual([(r.full_url, r.method) for r in requests], [(q.AUTH, 'GET'), (q.MANIFEST, 'HEAD')])
        self.assertEqual(requests[1].get_header('Authorization'), 'Bearer PRIVATE-TOKEN')
        self.assertEqual(auth.reads, [q.BODY_LIMIT + 1])
        self.assertEqual(head.reads, [])
        self.assertNotIn('PRIVATE', json.dumps(result))
        self.assertEqual(result['serviceDateUtc'], '2026-10-09T21:00:00+00:00')
        q.validate(result)

    def test_exhausted_stops(self):
        result, *_ = probe(remaining='0;w=21600')
        self.assertFalse(result['eligible'])
        self.assertEqual(result['reason'], 'QuotaExhausted')

    def test_http429_even_positive_remaining_rejects(self):
        result, *_ = probe(status=429)
        self.assertFalse(result['eligible'])
        self.assertEqual(result['reason'], 'QuotaRejected')

    def test_actual_http_error_headers_retained_without_body(self):
        fields = headers(RateLimit_Limit='100;w=21600', RateLimit_Remaining='0;w=21600', Retry_After='12', RateLimit_Reset='1791580000')
        error = urllib.error.HTTPError(q.MANIFEST, 429, 'PRIVATE', fields, io.BytesIO(b'PRIVATE'))
        opener = Mock();opener.open.side_effect = [Response(body=b'{"token":"PRIVATE"}'), error]
        result = q.probe_http(opener)
        self.assertEqual((result['registryHttpStatus'], result['retryAfter'], result['rateLimitReset']), (429, 12, 1791580000))
        self.assertFalse(result['eligible'])
        self.assertNotIn('PRIVATE', json.dumps(result))

    def test_unknown_quota_stops(self):
        opener = Mock();opener.open.side_effect = [Response(body=b'{"token":"x"}'), Response()]
        result = q.probe_http(opener)
        self.assertFalse(result['eligible'])
        self.assertEqual(result['reason'], 'QuotaUnknown')

    def test_malformed_quota_inverse_matrix(self):
        for remaining, limit in [('x','100;w=21600'), ('99;w=1','100;w=21600'), ('101;w=21600','100;w=21600'), ('1;w=0','100;w=0'), ('1;w=21600,PRIVATE','100;w=21600')]:
            with self.subTest(remaining=remaining):
                result, *_ = probe(remaining=remaining, limit=limit)
                self.assertFalse(result['eligible'])
                self.assertEqual(result['reason'], 'MalformedResponse')

    def test_duplicate_quota_header_rejects(self):
        result, *_ = probe(extra={'RateLimit-Remaining':'1;w=21600'})
        self.assertFalse(result['eligible'])

    def test_retry_after_date_and_no_invented_reset(self):
        result, *_ = probe(extra={'Retry-After':'Fri, 09 Oct 2026 22:00:00 GMT'})
        self.assertEqual(result['retryAfter'], '2026-10-09T22:00:00+00:00')
        self.assertIsNone(result['rateLimitReset'])

    def test_invalid_optional_headers_failclosed(self):
        for fields in [{'Date':'PRIVATE'}, {'Retry-After':'PRIVATE'}, {'RateLimit-Reset':'PRIVATE'}]:
            with self.subTest(fields=fields):
                result, *_ = probe(extra=fields)
                self.assertFalse(result['eligible'])
                self.assertNotIn('PRIVATE', json.dumps(result))

    def test_token_absent_malformed_and_oversize_stops_before_head(self):
        for body in [b'{}', b'{"token":"PRIVATE\\nHEADER"}', b'not-json', b'x'*(q.BODY_LIMIT+1)]:
            opener = Mock();opener.open.return_value = Response(body=body)
            result = q.probe_http(opener)
            self.assertFalse(result['eligible'])
            self.assertEqual(opener.open.call_count, 1)
            self.assertNotIn('PRIVATE', json.dumps(result))

    def test_auth_redirect_or_error_never_attempts_manifest(self):
        for status in (301, 302, 401, 429):
            error = urllib.error.HTTPError(q.AUTH, status, 'PRIVATE', headers(Location='https://alternate.invalid'), io.BytesIO(b'PRIVATE'))
            opener = Mock();opener.open.side_effect = error
            result = q.probe_http(opener)
            self.assertFalse(result['eligible'])
            self.assertEqual(opener.open.call_count, 1)
        self.assertIsNone(q.NoRedirect().redirect_request(None, None, 302, '', headers(), 'https://alternate.invalid'))

    def test_transport_error_is_closed_category(self):
        opener = Mock();opener.open.side_effect = urllib.error.URLError('PRIVATE-ENDPOINT')
        result = q.probe_http(opener)
        self.assertEqual(result['reason'], 'TransportUnavailable')
        self.assertNotIn('PRIVATE', json.dumps(result))

    def test_cache_actual_mocked_inspection_command(self):
        run = Mock(return_value=SimpleNamespace(returncode=0, stdout=b'sha256:'+b'a'*64+b'\n', stderr=b''))
        self.assertEqual(q.inspect_cache(run), 'Present')
        self.assertEqual(run.call_args.args[0], ['docker','image','inspect','redis:8.4-alpine','--format','{{.Id}}'])
        self.assertEqual(run.call_args.kwargs['timeout'], 3)

    def test_cache_absence_is_distinct_from_daemon_fault(self):
        for code, output, error, expected in [(1,b'',b'Error response from daemon: No such image: redis:8.4-alpine\n','Absent'),(1,b'',b'Cannot connect PRIVATE','Unavailable'),(1,b'',b'No such image: other','Unavailable'),(0,b'PRIVATE',b'','Unavailable')]:
            self.assertEqual(q.inspect_cache(Mock(return_value=SimpleNamespace(returncode=code, stdout=output, stderr=error))), expected)
        self.assertEqual(q.inspect_cache(Mock(side_effect=subprocess.TimeoutExpired('PRIVATE', 3))), 'Unavailable')

    def test_worker_deadline_and_stderr_not_retained(self):
        run = Mock(side_effect=subprocess.TimeoutExpired('PRIVATE', 20))
        self.assertEqual(q.bounded_probe(run)['reason'], 'WorkerUnavailable')
        self.assertEqual(run.call_args.kwargs['timeout'], 20)
        self.assertEqual(run.call_args.kwargs['stderr'], subprocess.DEVNULL)

    def test_worker_fabricated_positive_private_payload_and_nonzero_reject(self):
        forged = q.empty();forged.update(eligible=True, reason='QuotaAvailable', registryHttpStatus=200)
        private = q.empty();private['extra'] = 'PRIVATE'
        cache = q.empty();cache.update(eligible=True, reason='CacheAvailable', cache='Present')
        for data, code in [(forged,0),(private,0),(cache,0),(probe()[0],1)]:
            run = Mock(return_value=SimpleNamespace(returncode=code, stdout=json.dumps(data).encode()))
            result = q.bounded_probe(run)
            self.assertFalse(result['eligible'])
            self.assertEqual(result['reason'], 'WorkerUnavailable')

    def test_receipt_exclusive_stale_and_acquisition_race_no_network(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory)/'receipt.json';path.write_text('ORIGINAL')
            network = Mock();cache = Mock()
            self.assertEqual(q.main(['--receipt',str(path),'--check-local-cache'], network, cache), 1)
            self.assertEqual(path.read_text(), 'ORIGINAL');network.assert_not_called();cache.assert_not_called()

    def test_concurrent_receipt_acquisition_only_one_owner_reads(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory)/'receipt.json';barrier = threading.Barrier(2)
            network = Mock(return_value=probe()[0]);results = []
            def attempt():
                barrier.wait()
                results.append(q.main(['--receipt',str(path)],network))
            threads = [threading.Thread(target=attempt) for _ in range(2)]
            for thread in threads:thread.start()
            for thread in threads:thread.join(timeout=3)
            self.assertEqual(sorted(results),[0,1]);network.assert_called_once()
            self.assertTrue(json.loads(path.read_text())['eligible'])

    def test_snapshot_success_then_exhausted_no_stale_transfer(self):
        with tempfile.TemporaryDirectory() as directory:
            paths=[Path(directory)/'a.json',Path(directory)/'b.json']
            results=[probe()[0],probe(remaining='0;w=21600')[0]]
            for path,result in zip(paths,results):
                code=q.main(['--receipt',str(path)], Mock(return_value=result))
                self.assertEqual(code, 0 if result['eligible'] else 1)
            self.assertTrue(json.loads(paths[0].read_text())['eligible'])
            self.assertFalse(json.loads(paths[1].read_text())['eligible'])

    def test_cache_present_and_daemonerror_do_not_probe_network(self):
        for state, code in [('Present',0),('Unavailable',1)]:
            with tempfile.TemporaryDirectory() as directory:
                path=Path(directory)/'receipt.json';network=Mock()
                self.assertEqual(q.main(['--receipt',str(path),'--check-local-cache'], network, Mock(return_value=state)),code)
                network.assert_not_called()
                self.assertEqual(json.loads(path.read_text())['cache'],state)

    def test_cache_absent_continues_fixed_quota_probe(self):
        with tempfile.TemporaryDirectory() as directory:
            path=Path(directory)/'receipt.json';network=Mock(return_value=probe()[0])
            self.assertEqual(q.main(['--receipt',str(path),'--check-local-cache'],network,Mock(return_value='Absent')),0)
            network.assert_called_once();self.assertEqual(json.loads(path.read_text())['cache'],'Absent')

    def test_unknown_or_abbreviated_cli_no_probe_or_receipt(self):
        network=Mock()
        for args in [['--image','PRIVATE'],['--rece','PRIVATE'],['--receipt','PRIVATE','--proxy','PRIVATE']]:
            self.assertEqual(q.main(args,network),1)
        network.assert_not_called()


if __name__ == '__main__':
    unittest.main()
