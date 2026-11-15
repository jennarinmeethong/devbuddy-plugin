"""Add/remove one opt-in Claude Desktop MCP entry; preserve unrelated settings."""
import json
import os
from pathlib import Path
import shutil
import sys
import tempfile
from datetime import datetime, timezone

home = Path.home()
base = home / '.devbuddy-npx-test'
config = home / 'Library/Application Support/Claude/claude_desktop_config.json'
name = 'devbuddy-npx-bms-test'
entry = {
    'command': str(base / 'tools/node/bin/node'),
    'args': ['--use-system-ca', str(base / 'production-01b838f7afb5486db45cfe02219a0b24/src/clients/devbuddy-npx/dist/main.js'),
             'mcp-bridge', '--dir', str(home / 'Desktop/Works/BMS')],
    'env': {'DEVBUDDY_HOME': str(home / '.devbuddy')},
}
assert len(sys.argv) == 2 and sys.argv[1] in ('apply', 'remove', 'preview')
if sys.argv[1] == 'preview':
    print(json.dumps({name: entry}, indent=2))
    sys.exit(0)
for executable in (entry['command'], entry['args'][1]):
    assert Path(executable).is_file(), 'Required client file missing'
original = config.read_bytes()
data = json.loads(original)
servers = data.setdefault('mcpServers', {})
assert isinstance(servers, dict)
previous_entry = {**entry, 'args': entry['args'][1:]}
assert name not in servers or servers[name] in (entry, previous_entry), 'Existing entry differs; refusing overwrite'
if sys.argv[1] == 'apply':
    servers[name] = entry
else:
    servers.pop(name, None)
evidence = Path(tempfile.mkdtemp(prefix='claude-bms-config-', dir=base))
backup = evidence / 'claude_desktop_config.backup.json'
shutil.copy2(config, backup)
os.chmod(backup, 0o600)
temporary = None
try:
    with tempfile.NamedTemporaryFile(mode='w', encoding='utf-8', dir=config.parent,
                                     prefix='.devbuddy-preview-', delete=False) as stream:
        temporary = Path(stream.name)
        json.dump(data, stream, ensure_ascii=False, indent=2)
        stream.write('\n')
        stream.flush()
        os.fsync(stream.fileno())
    os.chmod(temporary, config.stat().st_mode & 0o777)
    assert config.read_bytes() == original, 'Config changed concurrently; refusing overwrite'
    os.replace(temporary, config)
finally:
    if temporary is not None and temporary.exists():
        temporary.unlink()
(evidence / 'result.json').write_text(json.dumps({
    'action': sys.argv[1], 'server': name, 'utc': datetime.now(timezone.utc).isoformat(),
    'backup': str(backup), 'projectFilesChanged': False,
}, indent=2) + '\n')
print('Configured ' + name + ': ' + sys.argv[1])
print('Private backup: ' + str(backup))
print('Fully quit and reopen Claude when convenient to load the change.')
