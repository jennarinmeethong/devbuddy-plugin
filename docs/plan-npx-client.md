# แผนสร้าง DevBuddy npm client ให้ครบทุก feature ของ .NET client

วันที่: 2026-10-25  
เชื่อมกับ: [Phase 14.21](plan-phase-14.md#1421--opt-in-npm-client-approved-2026-10-24), [ADR-0016](adr/0016-node-client-feasibility.md)

สถานะ implementation วันที่ 2026-10-25: **B–E กำลังดำเนินการ; F ยังไม่เริ่ม** มี CLI,
registry, credential adapters, header helper และ bridge แล้ว พร้อม unit/Node/packed tests
บน Windows x64 แต่ยังไม่ถือว่าผ่านเกณฑ์ครบทุก platform/host ดูหลักฐานและรายการที่ยังขาดใน
[contract checklist](npx-client-contract.md) และ [คู่มือติดตั้ง/rollback](operations/npm-client.md)

ข้อมูลเจ้าของสำหรับ release: [แบบที่กรอกแล้ว](operations/npm-release-owner-details.md)
ชื่อแพ็กเกจที่เลือกคือ `devbuddy-plugin-client`; executable ยังคง `devbuddy-npx`
สิทธิ์เผยแพร่ต่อสาธารณะ, license, สิทธิ์ใช้ชื่อ npm และการอนุมัติ pilot ยังรอตรวจสอบ

## 1. เป้าหมาย สถานะปัจจุบัน และข้อตกลง

สร้าง client ที่ติดตั้งและเรียกผ่าน npm/npx ได้ โดยใช้คำสั่ง **`devbuddy-npx`** และรองรับความสามารถของ `.NET client` ปัจจุบันครบ ทั้งการจัดการ checkout, credential, header helper และ MCP bridge

**ด่าน A เสร็จแล้ว:** ต้นแบบผ่าน Windows x64/ARM64 ภายใต้ SAC enforcement, macOS Keychain และการติดตั้งผ่าน npx บน Linux x64/ARM64 ผลนี้ใช้เป็นฐานสำหรับเริ่มพัฒนา client เต็มชุด แต่ release ใหม่ยังต้องผ่านการทดสอบ artifact ของตัวเอง

ข้อตกลงที่คงไว้:

- เก็บ .NET client และ plugin default เดิมระหว่าง pilot
- ใช้ registry และ credential เดิมร่วมกัน ย้อนกลับไปใช้ .NET ได้
- Windows ใช้ Credential Manager, macOS ใช้ Keychain, Linux ใช้ไฟล์จำกัดสิทธิ์
- Product code ไม่เรียก process, shell, Git หรือ executable เดิม
- ติดต่อ DevBuddy ผ่าน HTTPS/MCP เท่านั้น
- คง operation catalog, server authorization และสิทธิ์ของ AI เดิม
- งานนี้ไม่เพิ่ม OAuth, การสร้าง token ผ่าน client, auto-update หรือการจัดการ server/deployment
- หน้าจอ Plugin access บนเว็บยังสร้างคำสั่งเดิม คู่มือ npm อธิบายวิธีแทน executable
- แพ็กเกจยังไม่ publish จนกว่าการพัฒนา การทดสอบ และ release checklist จะครบ

ข้อแก้ไขจากแผนแรกตาม implementation จริง: **`--json` อยู่ที่ `show` ส่วน `list` ปัจจุบันไม่มี JSON mode** แผนนี้จะรักษาสัญญานั้น

## 2. Features ที่ต้องทำครบ

### 2.1 คำสั่ง CLI

| คำสั่ง | Arguments/options | พฤติกรรมที่ต้องรองรับ |
|---|---|---|
| `register [path]` | `--server`, `--workspace` จำเป็น; `--project`, `--label` เลือกได้ | หา checkout root ตรวจ token/server/workspace แล้วบันทึก registration |
| `list` | ไม่มี option เพิ่ม | แสดง registrations ทุกตัว พร้อม label, default project และแจ้งโฟลเดอร์ที่หายไป |
| `show [path]` | `--json` | แสดง registration ที่ครอบโฟลเดอร์นั้น; แบบข้อความตรวจสถานะ token ได้ |
| `update [path]` | `--server`, `--workspace`, `--project`, `--clear-project`, `--label` | เปลี่ยน registration โดยตรวจ token ใหม่เมื่อเปลี่ยน server/workspace |
| `unregister [path]` | — | ลบ registration และเก็บ token ไว้สำหรับ checkout อื่น |
| `token set` | `--server`, `--workspace` จำเป็น | รับ token แบบซ่อนหรือจาก stdin ตรวจให้ผ่านก่อนแทนค่าที่เก็บ |
| `token remove` | `--server`, `--workspace` จำเป็น; `--force` | ป้องกันการลบ token ที่ยังมี checkout ใช้งาน และแจ้งให้ revoke ผ่านเว็บหากต้องการ |
| `doctor [path]` | — | ตรวจ registration, credential store, token, workspace และ MCP endpoint |
| `mcp-headers` | `--dir`, `--url` | ส่ง JSON Authorization header ให้ host เมื่อ origin ตรงกับ registration |
| `mcp-bridge` | `--server`, `--workspace`, `--project`, `--dir`, `--log` | ให้บริการ MCP stdio แล้วส่งต่อไป DevBuddy ผ่าน HTTP |
| `--help` | รองรับระดับ root และแต่ละคำสั่ง | แสดง syntax, required arguments และวิธีแก้ปัญหาเบื้องต้น |
| `--version` | — | แสดง version ของ npm client ที่กำลังรัน |

กติกาการใช้งาน:

- `path` ที่ละไว้ใช้ working directory
- `register` หา Git root ที่ใกล้ที่สุดจาก `.git` แบบ directory หรือไฟล์ โดยไม่รัน Git
- โฟลเดอร์เอกสารที่ไม่ได้เป็น Git repository ลงทะเบียนได้
- `update` และ `unregister` ใช้ registration ที่ตรงหรือครอบโฟลเดอร์ตาม client เดิม
- `--clear-project` มีผลเหนือ `--project` เช่นเดียวกับตัวเดิม
- label ถูก trim; `--label ""` ล้าง label
- ไม่เปิด response-file expansion สำหรับ argument ที่ขึ้นต้นด้วย `@`
- ไม่เพิ่ม `--token`, `--password` หรือ `--insecure`

**Exit codes**

| Code | ความหมาย |
|---|---|
| `0` | สำเร็จ |
| `1` | validation, parsing หรือ operation ล้มเหลวทั่วไป |
| `2` | ไม่พบ registration |
| `3` | URL ที่ helper ได้รับไม่ตรง server ที่ลงทะเบียน |
| `4` | ไม่มี token หรืออ่าน credential store ไม่ได้ใน helper/doctor |
| `130` | ผู้ใช้ยกเลิกด้วย Ctrl+C |

Bridge ที่จำเป็นต้องตอบปัญหาผ่าน MCP จะยังให้บริการเพื่ออธิบายเหตุผล แทนการออกทันทีทุกกรณี

### 2.2 Registry และความเข้ากันได้กับข้อมูลเดิม

ใช้ `~/.devbuddy/checkouts.json` และ `DEVBUDDY_HOME` เดิม โดยรักษา schema version 1:

```text
version
checkouts[]
  path
  server
  workspace
  project?
  label?
```

ต้องรองรับ:

- อ่านไฟล์ที่ .NET สร้าง และเขียนให้ .NET อ่านกลับได้
- GUID และ server origin ถูก normalize ให้ credential key ตรงกัน
- เลือก registration ที่ครอบโฟลเดอร์และมี path ยาวที่สุด
- เปรียบเทียบ path แบบเดิมของแต่ละ OS
- ตรวจ separator boundary เพื่อไม่ให้ `work` ครอบ `work-old`
- ปฏิเสธ drive root, home directory และ directory ที่ครอบ home
- รักษากติกา path ปัจจุบัน ไม่เปลี่ยน mapping เป็น `realpath` โดยเงียบ
- ไม่มี token อยู่ใน registry
- JSON เสียหรือ schema version ที่ไม่รองรับต้องแจ้ง error และรักษาไฟล์เดิม
- เขียนผ่าน temporary file ชื่อไม่ชนกัน แล้ว atomic replace
- Unix directory ใช้ `0700` และไฟล์ `0600`

ไม่เพิ่ม schema migration ใน release นี้ และไม่อ้างว่ารองรับการเขียน registry พร้อมกันระหว่าง .NET กับ Node แบบไร้ lost update เพราะ client เดิมยังไม่มี locking contract ร่วมกัน

**`show --json`**

รักษา fields เดิม:

```text
path
server
mcpUrl
workspaceId
projectId
note
```

คำสั่งนี้ต้องไม่อ่าน token, โหลด native credential module หรือติดต่อ server เพื่อให้ใช้จาก sandbox ได้

### 2.3 Credential และ token lifecycle

**Windows**

- ใช้ Koffi adapter ที่พิสูจน์แล้วกับ Credential Manager
- รักษา generic credential type, persistence, target key, username และ UTF-8 blob เดิม
- รองรับ x64 และ ARM64 พร้อมตรวจ struct layout
- ไม่มี file fallback อัตโนมัติ

**macOS**

- ใช้ Security.framework และ CoreFoundation ผ่าน adapter เดิม
- รักษา service `devbuddy` และ account key เดิม
- รองรับ create/read/update/delete และการอ่านข้าม .NET
- แยกกรณี item ไม่มี, Keychain ล็อก, interaction ไม่ได้ และผู้ใช้ปฏิเสธ
- ไม่ปลดล็อก Keychain หรือเปลี่ยนสิทธิ์ให้เอง
- คู่มืออธิบาย prompt ที่อาจเกิดหลังเปลี่ยน runtime หรืออัปเกรด

**Linux**

- เพิ่ม file store ให้เข้ากันได้กับ .NET
- ใช้ directory `tokens` เดิม
- ชื่อไฟล์เป็น SHA-256 ของ credential key ตามเดิม
- สร้าง temporary file ด้วยสิทธิ์จำกัดก่อนเขียน secret
- รองรับ read/write/delete และ missing-item behavior

**ทุก OS**

- คง `DEVBUDDY_CREDENTIAL_STORE=file` แบบ explicit สำหรับการทดสอบหรือผู้ใช้ที่เลือกเอง
- credential key ใช้ origin และ workspace ในรูปแบบเดิม
- token ต้องเป็น 43 ตัวอักษร base64url
- รับจาก hidden TTY prompt หรือบรรทัดแรกของ stdin
- รองรับ Enter, Backspace, EOF และ Ctrl+C โดยไม่ echo token
- ตรวจ server และ workspace ก่อนเก็บหรือแทน token
- registration ใหม่ใน workspace เดิมใช้ token ที่เก็บอยู่ได้
- หาก token เดิมถูกปฏิเสธ ให้แจ้งผู้ใช้ ไม่เปลี่ยน token เงียบ ๆ
- helper, bridge และ doctor ไม่ถาม token แบบ interactive
- ล้าง explicit secret buffers เมื่อทำได้ โดยไม่อ้างว่าสามารถล้าง JavaScript strings ได้แน่นอน

### 2.4 HTTP, TLS และ server verification

สร้าง HTTP transport กลางที่ใช้ร่วมกันระหว่างการตรวจ token และ bridge:

- ใช้ HTTPS; HTTP อนุญาตเฉพาะ loopback ตาม client เดิม
- ปฏิเสธ URL ที่มี username/password
- normalize scheme, host และ port เป็น origin
- bridge ส่งไป endpoint `/mcp` ของ origin ที่เลือก
- ไม่ติดตาม redirect พร้อม Authorization; แจ้งให้แก้ URL
- ตรวจ certificate ตามปกติ ไม่มีทางเลือกปิด TLS verification
- มีคู่มือใช้ CA ภายในองค์กรกับ Node โดยไม่ปิดการตรวจ certificate
- timeout ตรวจ token 20 วินาที และ bridge request 5 นาที
- ยกเลิก request เมื่อ process ถูกยกเลิก
- ไม่ retry tool call อัตโนมัติ เพราะบาง tool สร้าง draft หรือเปลี่ยนข้อมูล

การตรวจ token ต้องเรียก:

1. MCP `initialize`
2. `list_projects` ใน workspace ที่กำหนด
3. ตรวจทั้ง HTTP status และ MCP tool result

แยกผลให้ผู้ใช้เข้าใจได้:

- token ถูกปฏิเสธ
- token ใช้ผิด workspace
- ไม่มี MCP endpoint
- certificate ไม่เชื่อถือ
- network หรือ timeout
- rate limit
- response ไม่ใช่ MCP result ที่รองรับ

ไม่พิมพ์ raw response body หรือ argument ที่อาจเป็น secret ลง error/log

### 2.5 Header helper สำหรับ Claude Code และ Codex

`mcp-headers` ต้องทำครบดังนี้:

- ใช้ `--dir` หรือ working directory
- ใช้ `--url` ก่อน fallback เป็น `CLAUDE_CODE_MCP_SERVER_URL`
- หา registration ที่ตรงที่สุด
- ตรวจ origin ก่อนอ่านและส่ง token
- คืนเพียง JSON object ของ headers ทาง stdout เมื่อสำเร็จ
- เมื่อผิดพลาด stdout ต้องว่าง และเหตุผลอยู่ stderr
- รองรับ credential store ที่ถูก sandbox ปฏิเสธ โดยไม่ crash พร้อม stack trace
- ไม่มี banner, progress หรือข้อความ debug ปนใน protocol
- ใช้ได้จากโฟลเดอร์และ executable path ที่มีช่องว่างหรือภาษาไทย

การส่ง token ใน Authorization JSON เป็น output ที่ตั้งใจให้ host รับโดยเฉพาะ ส่วนคำสั่งอื่นและ logs ต้องไม่เผย token

### 2.6 MCP bridge สำหรับ Cowork และ host แบบ stdio

**เลือก server/workspace**

รักษาลำดับเดิม:

1. `--server` และ `--workspace` ที่ส่งมาคู่กัน
2. registration ที่ครอบโฟลเดอร์
3. registrations ทั้งเครื่อง โดยรวมตามคู่ `(origin, workspace)`

รองรับ:

- ค่าที่เป็น `${VARIABLE}` ซึ่งยังไม่ถูก expand ถือว่าไม่ได้ระบุ เช่นเดียวกับตัวเดิม
- มี target เดียว เลือกให้อัตโนมัติ
- มีหลาย target ต้องรอ `use_workspace`
- ไม่มี target ยัง initialize และตอบเหตุผลให้ assistant แสดงได้
- ใช้ label หรือชื่อโฟลเดอร์ช่วยแสดงชื่อ workspace
- รักษา default project เมื่อ registrations ของ workspace เดียวกันสอดคล้องกัน
- เลือก workspace ที่ไม่มี token ไม่ได้ แต่ยังเลือกตัวอื่นได้
- เลือกแล้วล็อกคู่ origin/workspace ตลอด process
- เรียกเลือกตัวเดิมซ้ำได้ แต่เปลี่ยน workspace กลาง session ไม่ได้
- หากชื่อหรือ identifier กำกวม ให้ตอบ error พร้อมรายการที่แยกได้ ไม่เลือกตัวแรกเอง

`use_workspace` เป็น tool เฉพาะ bridge ไม่เพิ่มเข้า server allow-list

**Workspace injection**

- ถอน `workspaceId` ออกจาก `properties` และ `required` ของ schema
- รองรับทั้ง argument ระดับบนและ `scope.workspaceId`
- จดจำตำแหน่งจาก `tools/list`
- รองรับ call ที่มาก่อน `tools/list` ตาม fallback ของ bridge เดิม
- เติมหรือแทน workspace ด้วยค่าที่ bridge เลือก
- assistant ส่ง workspace อื่นมาเองแล้วเปลี่ยนขอบเขตไม่ได้

**Protocol**

- รับ JSON-RPC แบบ UTF-8 หนึ่งข้อความต่อบรรทัด
- stdout ไม่มี BOM และใช้ LF
- รักษา request ID และ notification semantics
- JSON ไม่ถูกต้องตอบ parse error และไม่ส่งออก
- รอ `initialize` ให้จบก่อนส่งคำขอถัดไป เพื่อได้ negotiated protocol version
- ส่ง `MCP-Protocol-Version` ตาม version ที่เจรจาได้
- คำขอหลัง initialize ทำงานพร้อมกันได้
- จัดคิวการเขียน stdout และรองรับ backpressure เพื่อไม่ให้ JSON ปะปน
- รองรับ HTTP JSON และ SSE แบบหลาย events/multiline data
- ส่ง progress/notifications ตามลำดับภายใน stream
- รองรับ response ว่าง เช่น HTTP 202
- เมื่อ stdin ปิด รอ in-flight requests ตามพฤติกรรมเดิม; Ctrl+C ยกเลิกงาน
- อ่าน credential ใหม่ทุก request เพื่อรองรับ token replacement/removal
- รองรับ malformed response, connection interruption, timeout, 401/403, 404/405 และ 429
- เพิ่ม workspace/default-project guidance ใน initialize instructions โดยรักษาข้อความจาก server

พอร์ตตาม transport ของ server ปัจจุบัน ไม่เพิ่ม stateful MCP sessions, GET/DELETE transport, sampling หรือ elicitation ในงานย้าย client นี้

**Logging**

`--log` เก็บเฉพาะ timestamp, method, HTTP status และเหตุผลที่ปลอดภัย ไม่เก็บ token, headers, request arguments หรือ response content และไม่ dump environment ทั้งชุด

### 2.7 การใช้กับ plugin ทั้งสามตัว

| Host | งานที่ต้องทำ |
|---|---|
| Claude Code | คู่มือ opt-in ผ่าน `DEVBUDDY_CLIENT=devbuddy-npx`, ส่ง `--dir`/`--url` แบบ explicit และทดสอบ helper timeout |
| Codex | configuration ตัวอย่างที่เปลี่ยน header helper เป็น npm client พร้อม absolute-path fallback |
| Cowork | configuration ตัวอย่าง `devbuddy-npx mcp-bridge` และทดสอบ workspace selection จริง |

แนวทางติดตั้งที่แนะนำสำหรับ plugin คือ **ติดตั้ง exact version ล่วงหน้าแล้วเรียก executable โดยตรง** เพื่อไม่ให้การเชื่อมต่อขึ้นกับการดาวน์โหลดจาก npm

ยังรองรับ npx สำหรับการเรียกชั่วคราว:

```bash
npx --yes --ignore-scripts --package=devbuddy-plugin-client@<version> devbuddy-npx doctor
```

ทดสอบทั้ง:

- เปิด host จาก terminal และ GUI
- PATH ของ GUI ต่างจาก shell
- executable path มีช่องว่าง
- host ที่เรียก npm-generated `.cmd` ไม่ได้
- fallback ให้เรียก `node` กับ absolute path ของ entrypoint ที่ติดตั้งแล้ว
- cold/warm cache และการใช้งานหลังติดตั้งเมื่อ npm registry ไม่พร้อม

## 3. โครงสร้าง implementation และแพ็กเกจ

### 3.1 การแบ่งส่วน

คงโปรเจกต์ที่ `src/clients/devbuddy-npx` และแยกความรับผิดชอบเป็น:

| ส่วน | หน้าที่ |
|---|---|
| CLI/parser | arguments, help, exit codes และเรียก command handler |
| Context | streams, cwd, environment, filesystem, transport และ lazy credential factory |
| Registry/path rules | schema 1, checkout lookup และ atomic writes |
| Credential adapters | Windows, macOS และ Linux file store |
| Server verification | origin, TLS/HTTP และการตรวจ token/workspace |
| Header helper | สร้าง Authorization JSON ตาม registration |
| Bridge | JSON-RPC, SSE, concurrency, target selection และ workspace injection |

การตัดสินใจด้านเครื่องมือ:

- TypeScript strict และ ESM
- Node.js 24 พร้อม security patch ที่ผ่าน release checks
- Bun สำหรับ dependency installation, build และ unit tests
- Node จริงสำหรับ integration และ packed-artifact tests
- คง Koffi ที่ pin และผ่านการตรวจแล้ว
- ใช้ Node built-in argument/HTTP/stream APIs ก่อน ไม่เพิ่ม CLI framework หรือ MCP SDK ใน migration นี้
- โหลด native store แบบ lazy เพื่อให้ `--help`, `--version`, `list` และ `show --json` ไม่ต้องเข้าถึง native credentials

### 3.2 เปลี่ยนจาก prototype เป็น production artifact

- production `bin` มีคำสั่ง `devbuddy-npx`
- ย้าย credential probe และ diagnostics ไป feasibility/test build แยก
- ไม่ติดตั้งคำสั่งทดสอบ credential พร้อม production CLI
- เก็บเครื่องมือและหลักฐานด่าน A ไว้สำหรับ regression
- tarball มีเฉพาะ compiled production JS, runtime dependencies, README และ license
- ไม่มี tests, source bundle, `.env`, token, cache, logs หรือ remote evidence
- ผู้ใช้ไม่ต้องติดตั้ง Bun, .NET SDK หรือ C++ compiler
- แพ็กเกจของเราไม่มี install hook สำหรับดาวน์โหลด executable หรือ compile code
- ทุก platform ที่รองรับต้องติดตั้งด้วย `--ignore-scripts` และใช้งานได้
- ตรวจ dependency lifecycle scripts ด้วย เพื่อยืนยันว่า default installation ไม่ต้อง compile บน supported platforms

### 3.3 Version และ publication

- ชื่อเสนอคงเป็น `devbuddy-plugin-client`; command คง `devbuddy-npx`
- ตรวจชื่อและสิทธิ์ publisher อีกครั้งก่อนเผยแพร่ การได้ E404 ไม่ใช่การจองชื่อ
- หากใช้ชื่อไม่ได้ หยุดที่ release prerequisite ให้เจ้าของระบุชื่อที่ควบคุมได้
- version สอดคล้องกับ DevBuddy release ที่อนุมัติ
- pilot ใช้ prerelease version และ dist-tag `next`
- ไม่ใช้ `latest` ใน plugin configuration
- ใช้ GitHub Actions OIDC Trusted Publishing และ provenance หลังตั้ง publisher แล้ว ตาม [แนวทาง npm](https://docs.npmjs.com/trusted-publishers/)
- ตรวจ source commit, tag, package version และ artifact hash ให้ตรงกัน
- bootstrap publish ครั้งแรกและการตั้ง trusted publisher อยู่ใน checklist ของ release owner
- ยังไม่เปลี่ยน plugin default หรือเอา .NET client ออกเมื่อเผยแพร่ pilot

### 3.4 การใช้ร่วมกันและ rollback

- ไม่มีการย้ายหรือ export token ออกเป็นข้อความเพื่อเปลี่ยน client
- npm client อ่าน credential เดิมได้ทันทีเมื่อ OS อนุญาต
- .NET ต้องอ่าน registry/token ที่ npm เขียนกลับได้
- ทดสอบ update registration และ rotate token สลับ client
- rollback ด้วยการเปลี่ยน helper/bridge กลับเป็น `devbuddy` แล้วเริ่ม session ใหม่
- การถอน npm package ไม่ลบ registry หรือ credential อัตโนมัติ
- คงข้อจำกัดเรื่อง concurrent registry writers ไว้ในคู่มือ

## 4. แผนทดสอบและเกณฑ์ตรวจรับ

### 4.1 Contract และ regression tests

สร้าง feature checklist ที่ผูกแต่ละพฤติกรรมกับ test และใช้ .NET client ปัจจุบันเป็น baseline

| กลุ่ม | กรณีที่ต้องพิสูจน์ |
|---|---|
| CLI | ทุกคำสั่ง/options, required arguments, unknown arguments, help/version, exit codes และไม่มี response-file expansion |
| Registration | Git root, worktree `.git` file, plain folder, nested registration, missing folder และ broad-root refusal |
| Registry | .NET ↔ Node อ่านเขียนร่วมกัน, schema ผิด, JSON เสีย, optional fields, permissions และ failure ระหว่าง replace |
| Token | hidden input, stdin, EOF/Ctrl+C, format ผิด, token ถูกปฏิเสธ, workspace ผิด และเก็บ token เดิมเมื่อ verification ล้มเหลว |
| Token lifecycle | reuse ข้าม checkout, update workspace, unregister ไม่ลบ token และ remove ต้องมี force เมื่อยังใช้อยู่ |
| Sandbox | `show --json` ไม่แตะ store, show แบบข้อความไม่ crash เมื่อ store อ่านไม่ได้ และ helper stdout ว่างเมื่อถูกปฏิเสธ |
| Origin/TLS | default port, IPv6 loopback, userinfo, HTTP ภายนอก, wrong origin, redirect และ CA ไม่เชื่อถือ |
| Helper | JSON header ถูกต้อง, fallback environment, cwd/dir และไม่มี output อื่นปะปน |
| Bridge routing | target precedence, explicit server/workspace, unresolved variables และไม่มี registration |
| Multi-workspace | รอการเลือก, token หาย, ชื่อกำกวม, เลือกซ้ำ และป้องกันเปลี่ยน origin/workspace กลาง session |
| Workspace injection | top-level, nested scope, ก่อน/หลัง tools/list และแทนค่าที่ assistant ส่งมา |
| Protocol | initialize barrier, concurrent calls, notifications, JSON/SSE, multiline events, UTF-8 ภาษาไทย, EOF และ cancellation |
| Diagnostics | HTTP errors, timeout, malformed response และไม่เผย token/payload ใน logs |
| Packaging | local/global install, npx cold/warm, path ภาษาไทย, scripts disabled, ไม่มี compiler และไฟล์ใน tarball ถูกต้อง |
| Rollback | npm → .NET และ .NET → npm โดยใช้ข้อมูลเดิม |

ใช้ fake transport/store สำหรับ unit tests และใช้ server/OS store จริงสำหรับ integration ไม่ใช้ผล fake แทน native verification

### 4.2 Security checks

- ขยาย source guard ให้ครอบคลุม production TypeScript/JavaScript ทุกโฟลเดอร์
- ตรวจ process APIs, shell execution, dynamic execution และ caller-selected native libraries
- FFI อยู่เฉพาะ adapters ที่กำหนด และโหลด OS libraries แบบ literal
- ตรวจว่า error จาก arguments หรือ server ไม่สะท้อน secret
- ทดสอบว่า wrong origin ไม่อ่านและไม่ส่ง credential
- ทดสอบว่า malformed input ไม่ทำให้เกิด network call
- ทดสอบว่า workspace ที่ assistant ใส่มาไม่เปลี่ยนขอบเขต
- mutation-check guards โดยแก้กฎให้ผิดจริง ดู test ล้ม แล้วคืนโค้ด
- อัปเดต verification matrix ตามหลักฐานจริง ไม่เลื่อนสถานะจาก build ผ่านเพียงอย่างเดียว
- แยก npm provenance ออกจาก Windows signing/SAC evidence

### 4.3 Platform matrix

| Platform | เครื่อง/สภาพแวดล้อม | การตรวจรับ |
|---|---|---|
| Windows x64 | เครื่องปัจจุบัน | CLI, registry/credential parity, helpers, packed install และ SAC enforcement |
| Windows ARM64 | VM บน Mac mini | ชุดเดียวกับ x64 โดยใช้ Node/native module ARM64 |
| macOS ARM64 | Mac mini | Keychain prompts, cross-client parity, CLI และ packed client จาก desktop session |
| Linux x64 glibc | `devrelease` บน Proxmox | File store, permissions, registry parity, CLI/bridge และ packed install |
| Linux ARM64 glibc | Ubuntu VM | ชุดเดียวกับ Linux x64 แบบ native ARM64 |
| Linux musl x64/ARM64 | Alpine containers บนเครื่องสถาปัตยกรรมตรง | Runtime, file permissions และ packed client โดยไม่ใช้ .NET production executable |

ใช้ Node Alpine image ที่ pin สำหรับ musl drills และไม่ถือผล glibc แทน musl เพราะใช้ libc ต่างกัน ตาม [เอกสาร Node Docker](https://github.com/nodejs/docker-node/blob/main/README.md)

macOS x64 ยังไม่ประกาศเป็น verified platform ใน release แรก เพราะไม่มีเครื่องตรวจรับในแผนนี้

### 4.4 End-to-end กับ DevBuddy จริง

ใช้ throwaway stack และ synthetic workspace/token:

1. register ผ่าน npm client และตรวจ registry/token ด้วย .NET
2. เชื่อม Claude Code, Codex และ Cowork
3. initialize, list tools และเรียก tool จริง
4. ทดสอบทั้ง response ปกติและ streamed response
5. เปลี่ยน token และตรวจว่า bridge ใช้ค่าล่าสุด
6. revoke token แล้วต้องถูกปฏิเสธ
7. ลอง workspace ผิดและ multi-workspace session
8. ทดสอบ server unavailable, rate limit และ TLS CA ภายใน
9. เปลี่ยนกลับ .NET client และใช้งานข้อมูลเดิมได้
10. ยืนยันว่า catalog และ server allow-list ไม่เปลี่ยน

ตัวทดสอบจะไม่ใช้ workspace จริงที่มีข้อมูลลูกค้า และไม่แก้ deployment ที่ใช้งานอยู่

## 5. ลำดับส่งมอบ เกณฑ์จบ และ release

แบ่งเป็นงานที่ review ได้ทีละส่วน โดยทุกส่วนเพิ่ม tests ของตัวเอง:

| ชุดงาน | สิ่งส่งมอบ | เกณฑ์จบ |
|---|---|---|
| **B0 — Contract baseline** | feature checklist, CLI/output fixtures และ test mapping จาก .NET | ทุก feature ในข้อ 2 มีเจ้าของ test; บันทึกความต่างที่ตั้งใจ |
| **B1 — Registry/context** | lazy context, path rules, schema 1 และ atomic persistence | readonly commands ไม่แตะ native store; registry อ่านเขียนข้าม client ผ่าน |
| **B2 — Credentials/input** | production adapters, Linux file store และ hidden prompt | token lifecycle และ native/file compatibility ผ่านทุก platform ที่เกี่ยวข้อง |
| **B3 — CLI** | register/list/show/update/unregister/token/doctor/help/version | feature parity และ exit/output contracts ผ่านครบ |
| **C1 — HTTP/helper** | token verification, TLS/error mapping และ mcp-headers | helper ปลอดภัยและทำงานกับ Claude/Codex จริง |
| **C2 — Bridge** | routing, workspace choice/injection, SSE, concurrency และ logging | bridge contract tests และ Cowork integration ผ่าน |
| **D — Host integration/docs** | ตัวอย่าง opt-in ของสาม host, คู่มือติดตั้ง/update/rollback/troubleshooting | ใช้จาก GUI/terminal ได้จริง และ default เดิมยังทำงาน |
| **E1 — Production package** | แยก probe, production bin, clean tarball และ SBOM | ติดตั้งได้โดยไม่ใช้ Bun/.NET/compiler และไม่มีไฟล์ลับหลุด |
| **E2 — CI/release gates** | platform jobs, security checks, artifact tests และ OIDC workflow | required checks ผ่าน พร้อมหลักฐาน native/SAC ที่เกี่ยวข้อง |
| **F — Pilot** | prerelease `next`, published-artifact smoke tests และ release notes | ติดตั้งจาก npm จริง ใช้ครบและ rollback ได้ โดย owner อนุมัติ release |

เอกสารที่ต้องอัปเดตระหว่าง implementation:

- [Phase 14.21](plan-phase-14.md#1421--opt-in-npm-client-approved-2026-10-24): สถานะรายงานและ dated verification
- [ADR-0016](adr/0016-node-client-feasibility.md): การเปลี่ยนจาก feasibility เป็น production client
- `info.md`: การตัดสินใจใหม่ที่เจ้าของยืนยัน
- คู่มือ npm client, plugin-host instructions และ security verification matrix

**เกณฑ์ถือว่า “ครบทุก features”**

- คำสั่งและพฤติกรรมในข้อ 2 ใช้งานได้ครบ
- ไม่มี regression ที่ทำให้ใช้ registry/token ร่วมกับ .NET ไม่ได้
- Claude Code, Codex และ Cowork ใช้งานผ่าน npm client ได้จริง
- production artifact ผ่าน platform/security/release checks
- มีคู่มือ migration และ rollback ที่ทดลองแล้ว
- ไม่มี TODO ที่ซ่อนอยู่ใน feature ที่ประกาศรองรับ
- ข้อจำกัดที่ยังอยู่ระบุใน release notes
- การ publish และการเปลี่ยน default เป็นคนละขั้นตอน; pilot ไม่เปลี่ยนผู้ใช้เดิมอัตโนมัติ
