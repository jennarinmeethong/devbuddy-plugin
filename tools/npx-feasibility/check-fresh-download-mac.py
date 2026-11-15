"""Owner-run, read-only security observations plus isolated npm/native smoke.

Download and expand Node through the browser/Finder first. Never changes quarantine,
Gatekeeper policy, trust settings, registrations or credentials.
"""
import hashlib
import json
import os
from pathlib import Path
import subprocess
import tempfile

home = Path.home()
base = home / '.devbuddy-npx-test'
distribution = home / 'Downloads/node-v24.22.1-darwin-arm64'
node = distribution / 'bin/node'
archive = home / 'Downloads/node-v24.22.1-darwin-arm64.tar.gz'
package = base / 'devbuddy-plugin-client-1.14.0-next.0.tgz'
assert node.is_file(), 'First download and expand Node in Downloads using the browser/Finder.'
assert archive.is_file(), 'Keep the original .tar.gz in Downloads for checksum verification.'
def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()
assert digest(archive) == '89c682593d76cd44233fa53b1563264e7de9cbb189cad169135c5b4fadfbc0a7', 'Node archive checksum mismatch'
assert digest(package) == '62d1ebcab798e227e0c514f88bc28a38df30013e84b9d883209fccd8977fcfda', 'Client artifact checksum mismatch'
evidence = Path(tempfile.mkdtemp(prefix='fresh-download-', dir=base))
print('Evidence: ' + str(evidence), flush=True)
env = {k: v for k, v in os.environ.items()
       if not k.startswith(('NODE_', 'DEVBUDDY_', 'npm_config_', 'NPM_CONFIG_'))}
env.update(PATH=str(distribution / 'bin') + ':/usr/bin:/bin:/usr/sbin:/sbin',
           DEVBUDDY_HOME=str(home / '.devbuddy'),
           npm_config_userconfig=str(evidence / 'empty.npmrc'),
           npm_config_cache=str(evidence / 'cache'))
(evidence / 'empty.npmrc').write_text('')
report = {'nodeArchiveSha256': digest(archive), 'clientArtifactSha256': digest(package),
          'observations': {}, 'runtimePassed': False, 'policyChanged': False}
def observe(label, args):
    result = subprocess.run(args, env=env, capture_output=True, text=True, timeout=30)
    report['observations'][label] = {'exit': result.returncode,
                                    'stdout': result.stdout, 'stderr': result.stderr}
    return result
def save():
    (evidence / 'result.json').write_text(json.dumps(report, indent=2) + '\n')
for label, path in [('archive', archive), ('distribution', distribution), ('node', node)]:
    observe(label + 'Quarantine', ['/usr/bin/xattr', '-p', 'com.apple.quarantine', str(path)])
observe('gatekeeperStatus', ['/usr/sbin/spctl', '--status'])
observe('nodeSignature', ['/usr/bin/codesign', '--verify', '--strict', '--verbose=2', str(node)])
observe('nodeAssessment', ['/usr/sbin/spctl', '--assess', '--type', 'execute', '--verbose=2', str(node)])
report['quarantineObservedOnNode'] = report['observations']['nodeQuarantine']['exit'] == 0
save()
print('Quarantine on new Node: ' + str(report['quarantineObservedOnNode']), flush=True)
print('Review any macOS prompt normally. If blocked, stop; do not remove quarantine or use Open Anyway for this baseline.', flush=True)
try:
    def checked(args, cwd=evidence, timeout=300):
        result = subprocess.run(args, cwd=cwd, env=env, capture_output=True, text=True, timeout=timeout)
        assert result.returncode == 0, 'Command failed (exit %s); no security bypass attempted.' % result.returncode
        return result.stdout
    report['nodeVersion'] = checked([str(node), '--version']).strip()
    (evidence / 'package.json').write_text('{"name":"fresh-download-drill","private":true,"version":"0.0.0"}')
    npm = distribution / 'lib/node_modules/npm/bin/npm-cli.js'
    checked([str(node), str(npm), 'install', '--ignore-scripts', '--no-audit', '--no-fund', str(package)])
    entry = evidence / 'node_modules/devbuddy-plugin-client/dist/main.js'
    native = evidence / 'node_modules/@koromix/koffi-darwin-arm64/darwin_arm64/koffi.node'
    if native.exists():
        observe('nativeQuarantine', ['/usr/bin/xattr', '-p', 'com.apple.quarantine', str(native)])
        report['nativeSha256'] = digest(native)
    registry = home / '.devbuddy/checkouts.json'
    before = digest(registry)
    output = checked([str(node), '--use-system-ca', str(entry), 'doctor', str(home / 'Desktop/Works/BMS')])
    (evidence / 'doctor.txt').write_text(output)
    assert digest(registry) == before, 'Registry changed during the test; investigate.'
    report['registryUnchanged'] = True
    report['runtimePassed'] = True
    print(output, end='')
finally:
    save()
print('Finished. Runtime success is separate from Gatekeeper assessment; evidence: ' + str(evidence))
