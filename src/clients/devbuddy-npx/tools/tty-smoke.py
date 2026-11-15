"""Real Unix PTY + production CLI; synthetic HTTP/file credentials only."""
import hashlib
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
import json
import os
from pathlib import Path
import pty
import select
import shutil
import subprocess
import termios
import tempfile
import threading
import time
import uuid

root = Path(__file__).resolve().parent.parent
node = shutil.which('node')
assert node and os.name == 'posix', 'Use a Unix Node test toolchain'
(root / 'out').mkdir(exist_ok=True)
evidence = Path(tempfile.mkdtemp(prefix='tty-smoke-', dir=root / 'out'))
secret = 't' * 42 + 'u'
requests = []
class Handler(BaseHTTPRequestHandler):
    def log_message(self, *_):
        pass
    def do_POST(self):
        requests.append(1)
        message = json.loads(self.rfile.read(int(self.headers['Content-Length'])))
        assert self.headers['Authorization'] == 'Bearer ' + secret
        result = ({'protocolVersion': '2025-06-18', 'capabilities': {},
                   'serverInfo': {'name': 'synthetic-tty', 'version': '1'}}
                  if message['method'] == 'initialize' else {'content': []})
        data = json.dumps({'jsonrpc': '2.0', 'id': message['id'], 'result': result}).encode()
        self.send_response(200)
        self.send_header('Content-Type', 'application/json')
        self.send_header('Content-Length', str(len(data)))
        self.end_headers()
        self.wfile.write(data)
server = ThreadingHTTPServer(('127.0.0.1', 0), Handler)
threading.Thread(target=server.serve_forever, daemon=True).start()
origin = 'http://127.0.0.1:%s' % server.server_port
checks = []
cases = [
    ('hidden-enter', secret.encode() + b'\r', 0),
    ('hidden-backspace', secret[:-1].encode() + b'Z\x7f' + secret[-1:].encode() + b'\r', 0),
    ('empty-EOF', b'\x04', 1),
    ('typed-EOF', secret.encode() + b'\x04', 0),
    ('Ctrl-C', b'\x03', 130),
    ('oversized-input', b'x' * 4097, 1),
]
try:
    for name, typed, expected in cases:
        case = evidence / name
        case.mkdir()
        checkout = case / 'checkout'
        checkout.mkdir()
        (checkout / '.git').mkdir()
        home = case / 'home'
        workspace = str(uuid.uuid4())
        env = {**os.environ, 'DEVBUDDY_HOME': str(home), 'DEVBUDDY_CREDENTIAL_STORE': 'file'}
        master, slave = pty.openpty()
        before = termios.tcgetattr(slave)
        count = len(requests)
        child = None
        output = b''
        try:
            child = subprocess.Popen([node, str(root / 'dist/main.js'), 'register',
                                      '--server', origin, '--workspace', workspace],
                                     cwd=checkout, env=env, stdin=slave, stdout=slave, stderr=slave)
            sent = False
            deadline = time.monotonic() + 15
            while True:
                assert time.monotonic() < deadline, 'PTY case deadline exceeded: ' + name
                if select.select([master], [], [], 0.05)[0]:
                    output += os.read(master, 65536)
                if not sent and b'Machine token (hidden): ' in output:
                    flags = termios.tcgetattr(slave)[3]
                    assert not flags & termios.ECHO, 'Input echo was enabled: ' + name
                    assert not flags & termios.ICANON, 'Raw input was not enabled'
                    os.write(master, typed)
                    sent = True
                if child.poll() is not None:
                    while select.select([master], [], [], 0)[0]:
                        output += os.read(master, 65536)
                    break
            assert sent, 'No hidden-input prompt'
            assert child.returncode == expected, 'Unexpected CLI exit for ' + name
            assert termios.tcgetattr(slave) == before, 'Terminal attributes not restored: ' + name
            assert secret.encode() not in output and b'x' * 32 not in output, 'Input echoed'
            key = 'devbuddy:%s/%s' % (origin, workspace)
            stored = home / 'tokens' / hashlib.sha256(key.encode()).hexdigest()
            if expected == 0:
                assert stored.read_text().strip() == secret
                assert len(requests) - count == 2
            else:
                assert not stored.exists() and not (home / 'checkouts.json').exists()
                assert len(requests) == count, 'Cancelled/refused input reached network'
            checks.append({'case': name, 'exit': expected, 'passed': True,
                           'echoDisabled': True, 'terminalRestored': True})
        finally:
            if child and child.poll() is None:
                child.kill()
                child.wait()
            os.close(master)
            os.close(slave)
finally:
    server.shutdown()
    server.server_close()
report = {'passed': True, 'platform': os.uname().sysname, 'architecture': os.uname().machine,
          'syntheticOnly': True, 'actualPty': True, 'checks': checks, 'evidence': str(evidence)}
(evidence / 'result.json').write_text(json.dumps(report, indent=2) + '\n')
print(json.dumps(report, indent=2))
