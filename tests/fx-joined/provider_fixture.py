"""Hosted-only UUID-isolated Frankfurter transport fixture; never contacts an upstream."""
import argparse
import json
import threading
import time
import uuid
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from urllib.parse import parse_qs, urlsplit


def serve(run_id: str, port: int) -> None:
    run_id = str(uuid.UUID(run_id))
    if port != 0 and not 1024 < port <= 65535:
        raise ValueError('Ephemeral loopback fixture port required')
    state = {'mode': 'valid', 'requests': []}
    lock = threading.Lock()
    prefix = '/runs/' + run_id

    class Handler(BaseHTTPRequestHandler):
        def log_message(self, *_args):
            pass  # Never log headers, credentials, body or tokens.

        def reply(self, status, payload):
            body = payload if isinstance(payload, bytes) else json.dumps(payload).encode()
            self.send_response(status)
            self.send_header('Content-Type', 'application/json')
            self.send_header('Content-Length', str(len(body)))
            self.end_headers()
            try:
                self.wfile.write(body)
            except (BrokenPipeError, ConnectionResetError):
                pass

        def owned(self):
            return self.headers.get('X-Fx-Fixture-Run') == run_id and not self.headers.get('Authorization')

        def do_GET(self):
            target = urlsplit(self.path)
            if not self.owned():
                self.reply(403, {'error': 'fixture-isolation'})
                return
            if target.path == prefix + '/control/stats':
                with lock:
                    snapshot = {'mode': state['mode'], 'requests': list(state['requests'])}
                self.reply(200, snapshot)
                return
            try:
                query = parse_qs(target.query, strict_parsing=True)
            except ValueError:
                self.reply(400, {'error': 'unexpected-provider-query'})
                return
            if target.path != prefix + '/latest' or query != {'amount': ['1'], 'from': ['THB'], 'to': ['USD']}:
                self.reply(400, {'error': 'unexpected-provider-contract'})
                return
            with lock:
                state['requests'].append({'method': 'GET', 'path': target.path, 'query': query})
                mode = state['mode']
            if mode == 'timeout':
                time.sleep(40)
            if mode == 'refusal':
                self.reply(503, {'error': 'controlled-refusal'})
            elif mode == 'invalid-json':
                self.reply(200, b'not-json')
            else:
                rate = {'zero': 0, 'negative': -0.025, 'changed': 0.03, 'malformed': 'not-a-rate'}.get(mode, 0.025)
                self.reply(200, {'base': 'THB', 'date': '2026-10-05',
                                 'rates': {} if mode == 'missing' else {'USD': rate}})

        def do_POST(self):
            if not self.owned() or self.path != prefix + '/control/scenario':
                self.reply(403, {'error': 'fixture-isolation'})
                return
            length = int(self.headers.get('Content-Length', '0'))
            if not 0 < length <= 256:
                self.reply(400, {'error': 'bounded-control-body-required'})
                return
            try:
                payload = json.loads(self.rfile.read(length))
                mode = payload['mode']
                if mode not in ('valid', 'changed', 'zero', 'negative', 'missing', 'malformed', 'invalid-json', 'refusal', 'timeout'):
                    raise ValueError('unknown scenario')
            except (ValueError, KeyError, TypeError):
                self.reply(400, {'error': 'invalid-scenario'})
                return
            with lock:
                state['mode'] = mode
                if payload.get('resetRequests') is True:
                    state['requests'].clear()
            self.reply(200, {'mode': mode})

    server = ThreadingHTTPServer(('127.0.0.1', port), Handler)
    print(json.dumps({'runId': run_id, 'providerOrigin': 'http://127.0.0.1:' + str(server.server_port) + '/'}), flush=True)
    try:
        server.serve_forever()
    finally:
        server.server_close()


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--run-id', required=True)
    parser.add_argument('--port', type=int, default=0)
    arguments = parser.parse_args()
    serve(arguments.run_id, arguments.port)
