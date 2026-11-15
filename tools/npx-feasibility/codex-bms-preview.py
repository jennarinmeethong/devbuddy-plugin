"""Add an opt-in Codex stdio entry using the installed Codex config writer."""
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile

home = Path.home()
base = home / '.devbuddy-npx-test'
codex = home / '.local/bin/codex'
config = home / '.codex/config.toml'
name = 'devbuddy-npx-bms-test'
node = base / 'tools/node/bin/node'
entry = base / 'production-01b838f7afb5486db45cfe02219a0b24/src/clients/devbuddy-npx/dist/main.js'
args = ['--use-system-ca', str(entry), 'mcp-bridge', '--dir', str(home / 'Desktop/Works/BMS')]
for path in (codex, config, node, entry):
    assert path.is_file(), 'Required installed file missing: ' + str(path)
result = subprocess.run([str(codex), 'mcp', 'list', '--json'], capture_output=True, text=True, check=True)
servers = json.loads(result.stdout)
assert not any(item['name'] == name for item in servers), 'Test entry already exists; inspect before replacing.'
evidence = Path(tempfile.mkdtemp(prefix='codex-bms-config-', dir=base))
backup = evidence / 'config.backup.toml'
shutil.copy2(config, backup)
os.chmod(backup, 0o600)
subprocess.run([str(codex), 'mcp', 'add', name, '--env', 'DEVBUDDY_HOME=' + str(home / '.devbuddy'),
                '--', str(node), *args], check=True)
result = subprocess.run([str(codex), 'mcp', 'get', name, '--json'], capture_output=True, text=True, check=True)
configured = json.loads(result.stdout)
transport = configured['transport']
assert transport['type'] == 'stdio'
assert transport['command'] == str(node) and transport['args'] == args
assert transport['env']['DEVBUDDY_HOME'] == str(home / '.devbuddy')
(evidence / 'result.json').write_text(json.dumps({
    'configured': True, 'name': name, 'transport': 'stdio', 'node': str(node), 'args': args,
    'backup': str(backup), 'toolInvocationVerified': False,
}, indent=2) + '\n')
print('Verified test entry. Private backup: ' + str(backup))
print('Existing MCP entries retained. Open a new Codex session on Mac to test.')
print('Remove test entry later: ' + str(codex) + ' mcp remove ' + name)
