// Test metadata only: never enumerates or reads real credential items.
import koffi from 'koffi';
import { basename } from 'node:path';
import { runProbe } from '../dist/probe.js';

if (process.platform !== 'darwin') throw new Error('macOS only');
const security = koffi.load('/System/Library/Frameworks/Security.framework/Security');
const foundation = koffi.load('/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation');
const copy = security.func('int SecKeychainCopyDefault(_Out_ void **keychain)');
const getStatus = security.func('int SecKeychainGetStatus(void *keychain, _Out_ uint32_t *status)');
const getPath = security.func('int SecKeychainGetPath(void *keychain, _Inout_ uint32_t *length, _Out_ char *path)');
const interaction = security.func('int SecKeychainGetUserInteractionAllowed(_Out_ uint8_t *allowed)');
const release = foundation.func('void CFRelease(void *object)');
const pointer = [null], flags = [0], allowed = [0];
const report = { copyStatus: Number(copy(pointer)), interactionStatus: Number(interaction(allowed)), interactionAllowed: Boolean(allowed[0]) };
if (report.copyStatus === 0) {
  try {
    report.statusResult = Number(getStatus(pointer[0], flags));
    report.flags = flags[0];
    const length = [4096], path = Buffer.alloc(4096);
    if (getPath(pointer[0], length, path) === 0) report.keychainName = basename(path.subarray(0, length[0]).toString('utf8').replace(/\0.*$/, ''));
  } finally { release(pointer[0]); }
}
try {
  await runProbe(['roundtrip']);
  report.probePassed = true;
} catch (error) {
  report.probePassed = false;
  report.osStatus = /OSStatus (-?\d+)/.exec(error instanceof Error ? error.message : '')?.[1] ?? 'unavailable';
  report.errorCode = typeof error?.code === 'string' ? error.code : null;
  report.errorClass = error instanceof Error ? error.name : 'unknown';
  report.fileName = typeof error?.path === 'string' ? basename(error.path) : null;
}
process.stdout.write(`${JSON.stringify(report, null, 2)}\n`);
