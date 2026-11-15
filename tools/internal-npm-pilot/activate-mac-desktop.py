"""Apply only the owner-requested BMS Claude Desktop MCP switch, with private backup.

No plugin uninstall, app restart, project file change or credential operation.
"""
import json
import os
from pathlib import Path
import shutil
import sys
import tempfile

assert len(sys.argv) == 2, 'Pass the prepared pilot output folder'
home = Path.home()
pilot = Path(sys.argv[1]).resolve()
assert pilot.is_relative_to(home / '.devbuddy-pilots'), 'Use the owner pilot folder'
candidate = json.loads((pilot / 'claude-desktop.json').read_text())['mcpServers']['devbuddy']
expected_checkout = str(home / 'Desktop/Works/BMS')
assert candidate['args'][-2:] == ['--dir', expected_checkout]
assert candidate['args'][0] == '--use-system-ca' and candidate['args'][2] == 'mcp-bridge'
assert Path(candidate['command']).is_file() and Path(candidate['args'][1]).is_file()
assert candidate['env'] == {'DEVBUDDY_HOME': str(home / '.devbuddy')}
config = home / 'Library/Application Support/Claude/claude_desktop_config.json'
original = config.read_bytes()
data = json.loads(original)
servers = data.setdefault('mcpServers', {})
assert servers.get('devbuddy') in (None, candidate), 'Existing primary server differs; inspect before replacing'
old_name = 'devbuddy-npx-bms-test'
old = servers.get(old_name)
if old is not None:
    assert old['args'][-2:] == ['--dir', expected_checkout], 'Old test entry targets another folder'
    assert old['command'] == candidate['command'], 'Old test entry has an unexpected launcher'
    assert old['env'] == candidate['env'], 'Old test entry has an unexpected registry'
backup = Path(tempfile.mkdtemp(prefix='desktop-backup-', dir=home / '.devbuddy-pilots'))
shutil.copy2(config, backup / 'claude_desktop_config.backup.json')
os.chmod(backup / 'claude_desktop_config.backup.json', 0o600)
previous = {name: servers.get(name) for name in ('devbuddy', old_name)}
servers['devbuddy'] = candidate
servers.pop(old_name, None)
temporary = None
try:
    with tempfile.NamedTemporaryFile(mode='w', encoding='utf-8', dir=config.parent,
                                     prefix='.devbuddy-pilot-', delete=False) as stream:
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
assert json.loads(config.read_text())['mcpServers']['devbuddy'] == candidate
(backup / 'rollback-entries.json').write_text(json.dumps(previous, indent=2) + '\n')
os.chmod(backup / 'rollback-entries.json', 0o600)
report = {'configured': True, 'host': 'Claude Desktop', 'checkout': expected_checkout,
          'primaryServer': 'devbuddy', 'removedOldTestEntry': old is not None,
          'installedEntry': candidate['args'][1], 'backup': str(backup),
          'dotnetUninstalled': False, 'userScopeCodePluginChanged': False,
          'credentialsChanged': False, 'registryChanged': False, 'appRestarted': False,
          'liveToolCallVerified': False}
(pilot / 'activation-result.json').write_text(json.dumps(report, indent=2) + '\n')
print(json.dumps(report, indent=2))
