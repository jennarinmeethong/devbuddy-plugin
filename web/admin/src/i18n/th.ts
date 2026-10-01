/**
 * Thai, keyed by the English it replaces. `i18n.test.tsx` holds this equal to the source: every
 * key the client uses is here, nothing here is unused, and every `{placeholder}` survives.
 *
 * Terms, so two screens do not call one thing by two names: workspace เวิร์กสเปซ, project
 * โปรเจกต์, record บันทึก, work item งาน, evidence หลักฐาน, draft ฉบับร่าง, revision ฉบับแก้ไข,
 * publish เผยแพร่, approve อนุมัติ, archive เก็บถาวร, grant สิทธิ์, role บทบาท, scope ขอบเขต,
 * token โทเค็น, secret ข้อมูลลับ, redact ปกปิด, assistant ผู้ช่วย AI, audit การตรวจสอบย้อนหลัง.
 * Names of things in code (DEVBUDDY_TOKEN, SMTP, GitHub, Claude, Codex, Markdown) stay as they are.
 */
export const th: Record<string, string> = {
  // App and shell
  "Loading…": "กำลังโหลด…",
  Language: "ภาษา",
  "You do not have access to this workspace, or it does not exist. Both look the same from here on purpose.":
    "คุณไม่มีสิทธิ์เข้าถึงเวิร์กสเปซนี้ หรือเวิร์กสเปซนี้ไม่มีอยู่ ทั้งสองกรณีแสดงผลเหมือนกันโดยเจตนา",
  Teams: "ทีม",
  Audit: "การตรวจสอบย้อนหลัง",
  Health: "สถานะระบบ",
  "Plugin access": "การเข้าถึงปลั๊กอิน",
  "Close menu": "ปิดเมนู",
  "DevBuddy administration": "ระบบจัดการ DevBuddy",
  "Open menu": "เปิดเมนู",

  // Labels for the server's values
  "Context reference": "ข้อมูลอ้างอิงบริบท",
  "Delivery state": "สถานะการส่งมอบ",
  Decision: "การตัดสินใจ",
  "Technical knowledge": "ความรู้ทางเทคนิค",
  "Change impact": "ผลกระทบของการเปลี่ยนแปลง",
  Handover: "การส่งมอบงาน",
  "Code review feedback": "ความเห็นจากการรีวิวโค้ด",
  "Written by a person": "เขียนโดยบุคคล",
  "A document": "เอกสาร",
  "Repository analysis": "การวิเคราะห์ repository",
  "Issue tracker": "ระบบติดตามปัญหา",
  "Pull request": "Pull request",
  "Written by an AI assistant": "เขียนโดยผู้ช่วย AI",
  Viewer: "ผู้ดู",
  Contributor: "ผู้ร่วมเขียน",
  Reviewer: "ผู้ตรวจทาน",
  Administrator: "ผู้ดูแลระบบ",
  "Index maintainer": "ผู้ดูแลดัชนี",

  // Analysis
  "The project as a whole": "โปรเจกต์ทั้งหมด",
  Code: "โค้ด",
  Documents: "เอกสาร",
  Architecture: "สถาปัตยกรรม",
  "Git history": "ประวัติ Git",
  "Work items": "งาน",
  "Test evidence": "หลักฐานการทดสอบ",
  Analysis: "การวิเคราะห์",
  "Repositories this project can read": "Repository ที่โปรเจกต์นี้อ่านได้",
  "No repository is reachable for this project. An operator makes one reachable by mounting its working copy under the project's directory, named by a repository identifier, or by configuring it for the GitHub API. An analysis of the whole project can still run.":
    "โปรเจกต์นี้ยังเข้าถึง repository ใดไม่ได้ ผู้ดูแลระบบทำให้เข้าถึงได้โดย mount working copy ไว้ใต้ไดเรกทอรีของโปรเจกต์ โดยตั้งชื่อตามรหัสของ repository หรือตั้งค่าให้อ่านผ่าน GitHub API ส่วนการวิเคราะห์ทั้งโปรเจกต์ยังทำได้ตามปกติ",
  Repository: "Repository",
  Address: "ที่อยู่",
  "Mounted working copy": "Working copy ที่ mount ไว้",
  "The whole project directory": "ไดเรกทอรีของโปรเจกต์ทั้งหมด",
  "Run an analysis": "เรียกใช้การวิเคราะห์",
  "What to look at": "สิ่งที่จะวิเคราะห์",
  "Path inside it": "พาธภายใน",
  "Optional. Relative, and it cannot leave the repository.":
    "ไม่บังคับ เป็นพาธแบบสัมพัทธ์ และออกนอก repository ไม่ได้",
  Analyse: "วิเคราะห์",
  "Nothing to report.": "ไม่มีสิ่งที่ต้องรายงาน",
  Subject: "หัวข้อ",
  Detail: "รายละเอียด",
  Where: "ตำแหน่ง",
  "What a change affects": "การเปลี่ยนแปลงนี้กระทบอะไรบ้าง",
  "Commit or range": "Commit หรือช่วง commit",
  "A commit, or two joined by .. such as main..feature.":
    "commit เดียว หรือสอง commit ที่คั่นด้วย .. เช่น main..feature",
  "Work out the impact": "ประเมินผลกระทบ",
  "Changed paths ({length})": "พาธที่เปลี่ยน ({length})",
  "Compare two references": "เปรียบเทียบสองจุดอ้างอิง",
  Earlier: "ก่อนหน้า",
  "A branch, a tag or a commit.": "branch, tag หรือ commit",
  Later: "ภายหลัง",
  Compare: "เปรียบเทียบ",
  "{earlier} is {earlierCommit}; {later} is {laterCommit}.":
    "{earlier} คือ {earlierCommit}; {later} คือ {laterCommit}",
  "No differences.": "ไม่มีความแตกต่าง",
  What: "สิ่งที่เปลี่ยน",
  Before: "ก่อน",
  After: "หลัง",
  "Synchronise a repository": "ซิงก์ repository",
  "Reads a snapshot of where the repository stands now. One way only: nothing is written back.":
    "อ่านภาพรวมสถานะปัจจุบันของ repository เป็นทางเดียวเท่านั้น ไม่มีการเขียนกลับ",
  Synchronise: "ซิงก์",
  Commit: "Commit",
  "References recorded": "จุดอ้างอิงที่บันทึกไว้",
  "Open pull requests": "Pull request ที่เปิดอยู่",
  "Not available from a working copy": "ไม่มีข้อมูลนี้ใน working copy",
  "Open issues": "Issue ที่เปิดอยู่",

  // Audit
  Person: "บุคคล",
  Assistant: "ผู้ช่วย AI",
  "Internal system": "ระบบภายใน",
  "Audit history": "ประวัติการตรวจสอบย้อนหลัง",
  Project: "โปรเจกต์",
  "Choose a project…": "เลือกโปรเจกต์…",
  "Days back": "ย้อนหลัง (วัน)",
  Channel: "ช่องทาง",
  "Every channel": "ทุกช่องทาง",
  "People only": "เฉพาะบุคคล",
  "Assistants only": "เฉพาะผู้ช่วย AI",
  "Internal system only": "เฉพาะระบบภายใน",
  "Channel not recorded (before v1.3.0)": "ไม่ได้บันทึกช่องทาง (ก่อน v1.3.0)",
  "Choose a project to read its audit history.": "เลือกโปรเจกต์เพื่อดูประวัติการตรวจสอบย้อนหลัง",
  "Nothing in that window.": "ไม่มีรายการในช่วงเวลานี้",
  When: "เวลา",
  Action: "การกระทำ",
  Outcome: "ผลลัพธ์",
  Actor: "ผู้กระทำ",
  Resource: "ทรัพยากร",
  Succeeded: "สำเร็จ",
  "Not recorded": "ไม่ได้บันทึก",

  // Evidence
  Evidence: "หลักฐาน",
  "Attached to this project": "หลักฐานที่แนบกับโปรเจกต์นี้",
  "Nothing attached yet.": "ยังไม่มีหลักฐานที่แนบไว้",
  Type: "ประเภท",
  Size: "ขนาด",
  Captured: "เวลาที่แนบ",
  State: "สถานะ",
  "Fetching…": "กำลังดึงข้อมูล…",
  Download: "ดาวน์โหลด",
  "Attach an artefact": "แนบไฟล์หลักฐาน",
  File: "ไฟล์",
  "What it shows": "ไฟล์นี้แสดงอะไร",
  "The build log for release 1.4": "log การ build ของ release 1.4",
  "The file is scanned before it is stored. One carrying a credential is refused, and nothing is kept.":
    "ไฟล์จะถูกสแกนก่อนจัดเก็บ ไฟล์ที่มีข้อมูลรับรองการเข้าถึงจะถูกปฏิเสธ และจะไม่มีการเก็บสิ่งใดไว้",
  "Attaching…": "กำลังแนบ…",
  Attach: "แนบ",
  "{bytes} B": "{bytes} B",
  "{size} KB": "{size} KB",
  "{size} MB": "{size} MB",

  // Health
  "System health": "สถานะระบบ",
  Healthy: "ปกติ",
  Degraded: "ทำงานได้บางส่วน",
  "Checking…": "กำลังตรวจสอบ…",
  Component: "ส่วนประกอบ",
  Up: "ทำงาน",
  Down: "ไม่ทำงาน",
  Backup: "สำรองข้อมูล",
  "Writes a backup of every workspace to the server's backup volume. Copy it off that volume to keep it; restoring is done from the console.":
    "เขียนข้อมูลสำรองของทุกเวิร์กสเปซลงใน volume สำรองข้อมูลของเซิร์ฟเวอร์ หากต้องการเก็บไว้ ให้คัดลอกออกจาก volume นั้น ส่วนการกู้คืนทำผ่าน console",
  "Back up now": "สำรองข้อมูลตอนนี้",
  "Backup {reference}, {size} bytes, at {when}.": "ข้อมูลสำรอง {reference} ขนาด {size} ไบต์ เมื่อ {when}",

  // Maintenance
  Maintenance: "การบำรุงรักษา",
  "Quality sweeps": "ตรวจคุณภาพ",
  "Each sweep reads the project's records and reports what it finds. Nothing is changed.":
    "การตรวจแต่ละแบบจะอ่านบันทึกของโปรเจกต์แล้วรายงานสิ่งที่พบ โดยไม่เปลี่ยนแปลงข้อมูลใด",
  "Check provenance": "ตรวจที่มา",
  "Find duplicates": "หาบันทึกซ้ำ",
  "Untouched for (days)": "ไม่มีการแก้ไขมา (วัน)",
  "Find stale records": "หาบันทึกที่ล้าสมัย",
  Provenance: "ที่มา",
  Duplicates: "บันทึกซ้ำ",
  "Stale records": "บันทึกที่ล้าสมัย",
  "Nothing found.": "ไม่พบรายการ",
  Record: "บันทึก",
  Rule: "กฎ",
  "Search index": "ดัชนีการค้นหา",
  "Rebuilds this project's full-text index from its records. The semantic index is kept by the embedding worker, not by this.":
    "สร้างดัชนีค้นหาข้อความของโปรเจกต์นี้ใหม่จากบันทึก ส่วนดัชนีค้นหาตามความหมายดูแลโดย embedding worker ไม่ใช่ปุ่มนี้",
  "Rebuild the index": "สร้างดัชนีใหม่",
  "{documentsIndexed} record(s) indexed.": "ทำดัชนีแล้ว {documentsIndexed} บันทึก",
  "Check text": "ตรวจข้อความ",
  Text: "ข้อความ",
  "Nothing typed here is stored.": "ข้อความที่พิมพ์ที่นี่จะไม่ถูกจัดเก็บ",
  "Look for secrets": "ค้นหาข้อมูลลับ",
  "Redact it": "ปกปิดข้อมูล",
  Line: "บรรทัด",
  Length: "ความยาว",
  "No secrets found.": "ไม่พบข้อมูลลับ",
  "{findingCount} finding(s) redacted.": "ปกปิดแล้ว {findingCount} รายการ",
  Export: "ส่งออก",
  "Writes a copy of this project — records, work items and evidence — to the server's export volume, where the retention sweep removes it when it expires.":
    "เขียนสำเนาของโปรเจกต์นี้ ทั้งบันทึก งาน และหลักฐาน ลงใน volume สำหรับส่งออกของเซิร์ฟเวอร์ เมื่อหมดอายุ ระบบจัดการระยะเวลาเก็บข้อมูลจะลบออก",
  "Export this project": "ส่งออกโปรเจกต์นี้",
  "Export {reference}: {records} record(s), {evidence} evidence item(s). Kept until {when}.":
    "ส่งออก {reference}: {records} บันทึก หลักฐาน {evidence} รายการ เก็บไว้ถึง {when}",

  // Members
  "Members of {name}": "สมาชิกของ {name}",
  "Members of this workspace": "สมาชิกของเวิร์กสเปซนี้",
  "No memberships.": "ยังไม่มีสมาชิก",
  User: "ผู้ใช้",
  Role: "บทบาท",
  Scope: "ขอบเขต",
  Granted: "ให้สิทธิ์เมื่อ",
  "Project {project}": "โปรเจกต์ {project}",
  "Whole workspace": "ทั้งเวิร์กสเปซ",
  Active: "ใช้งานอยู่",
  Revoked: "เพิกถอนแล้ว",
  Revoke: "เพิกถอน",
  "Revoked grants stay listed. Hiding them would make a revocation look like the grant never happened, and a revocation takes effect on the next request rather than the next sign-in.":
    "สิทธิ์ที่เพิกถอนแล้วยังแสดงอยู่ในรายการ เพราะหากซ่อนไว้จะดูเหมือนไม่เคยให้สิทธิ์นั้นเลย การเพิกถอนมีผลตั้งแต่คำขอถัดไป ไม่ต้องรอให้เข้าสู่ระบบใหม่",
  "New role for {user}": "บทบาทใหม่ของ {user}",
  "Change role": "เปลี่ยนบทบาท",
  "The old grant was revoked, but the new one was refused. Grant it again below.":
    "เพิกถอนสิทธิ์เดิมแล้ว แต่การให้สิทธิ์ใหม่ถูกปฏิเสธ ให้สิทธิ์อีกครั้งด้านล่าง",
  Downloads: "การดาวน์โหลด",
  "Downloads by {user}": "การดาวน์โหลดของ {user}",
  "Nothing downloaded or exported here in 90 days.": "ไม่มีการดาวน์โหลดหรือส่งออกในเวิร์กสเปซนี้ในช่วง 90 วัน",
  "downloaded evidence": "ดาวน์โหลดหลักฐาน",
  "exported a project": "ส่งออกโปรเจกต์",
  Close: "ปิด",
  "Reset password": "รีเซ็ตรหัสผ่าน",
  "Give them this reset token. It is shown once, is single-use, and expires. Their current sessions end when they use it.":
    "ส่งโทเค็นรีเซ็ตนี้ให้ผู้ใช้ โทเค็นแสดงเพียงครั้งเดียว ใช้ได้ครั้งเดียว และมีวันหมดอายุ เมื่อใช้แล้ว เซสชันที่เปิดอยู่ทั้งหมดของผู้ใช้จะสิ้นสุด",
  "Reset token": "โทเค็นรีเซ็ต",
  "Expires {when}. They redeem it on the set-password page.":
    "หมดอายุ {when} ผู้ใช้นำไปใช้ได้ที่หน้าตั้งรหัสผ่าน",
  "Issue reset token": "ออกโทเค็นรีเซ็ต",
  Cancel: "ยกเลิก",
  "Grant access to somebody already here": "ให้สิทธิ์แก่ผู้ที่อยู่ในเวิร์กสเปซนี้แล้ว",
  "The whole workspace": "ทั้งเวิร์กสเปซ",
  Grant: "ให้สิทธิ์",
  "Granted {role}.": "ให้สิทธิ์ {role} แล้ว",
  "Add somebody": "เพิ่มผู้ใช้",
  Email: "อีเมล",
  "Display name": "ชื่อที่แสดง",
  "Viewer to Administrator are cumulative: a reviewer can do everything a contributor can. IndexMaintainer is for a worker account: it reads and maintains the index, and nothing else.":
    "บทบาทตั้งแต่ผู้ดูถึงผู้ดูแลระบบมีสิทธิ์สะสมกัน เช่น ผู้ตรวจทานทำได้ทุกอย่างที่ผู้ร่วมเขียนทำได้ ส่วน IndexMaintainer มีไว้สำหรับบัญชี worker ซึ่งอ่านและดูแลดัชนีได้เท่านั้น",
  "Create account": "สร้างบัญชี",
  "The account exists and cannot be signed into until its owner sets a password. Give them this setup token — it is shown once, is single-use, and expires.":
    "สร้างบัญชีแล้ว แต่จะเข้าสู่ระบบไม่ได้จนกว่าเจ้าของบัญชีจะตั้งรหัสผ่าน ส่งโทเค็นตั้งค่านี้ให้ผู้ใช้ โทเค็นแสดงเพียงครั้งเดียว ใช้ได้ครั้งเดียว และมีวันหมดอายุ",
  "Expires {when}. It is also emailed to that address when the deployment has SMTP configured; when it does not, this is the copy.":
    "หมดอายุ {when} หากระบบตั้งค่า SMTP ไว้ โทเค็นจะถูกส่งไปยังอีเมลนั้นด้วย หากไม่ได้ตั้งค่า ที่นี่คือสำเนาเดียว",

  // Plugin access
  "Your plugin tokens for this workspace": "โทเค็นปลั๊กอินของคุณในเวิร์กสเปซนี้",
  "You have not issued any. Mint one below to connect Claude or Codex.":
    "คุณยังไม่ได้ออกโทเค็น สร้างโทเค็นด้านล่างเพื่อเชื่อมต่อ Claude หรือ Codex",
  Name: "ชื่อ",
  Issued: "ออกเมื่อ",
  Expires: "หมดอายุ",
  "Last used": "ใช้ล่าสุด",
  "Needs replacing": "ต้องออกใหม่",
  Ended: "สิ้นสุดแล้ว",
  "A token acts as you, with your permissions and no more, in this workspace and no other. A call naming a different workspace is refused even where you are a member, so if you work in more than one, mint a token in each. Revoking one takes effect on the next call, not the next restart — and never touches your password.":
    "โทเค็นทำงานในนามคุณ ด้วยสิทธิ์ของคุณเท่านั้น และใช้ได้เฉพาะในเวิร์กสเปซนี้ คำขอที่ระบุเวิร์กสเปซอื่นจะถูกปฏิเสธแม้คุณเป็นสมาชิกที่นั่น หากทำงานหลายเวิร์กสเปซ ให้สร้างโทเค็นแยกในแต่ละที่ การเพิกถอนมีผลตั้งแต่คำขอถัดไปโดยไม่ต้องรีสตาร์ต และไม่กระทบรหัสผ่านของคุณ",
  "The same token works in Claude and in Codex. It identifies you to DevBuddy and is not tied to, and does not verify, an account with either of them. A token per assistant is worth having only if you want to revoke or watch them separately.":
    "โทเค็นเดียวกันใช้ได้ทั้งใน Claude และ Codex โทเค็นใช้ยืนยันตัวตนของคุณกับ DevBuddy เท่านั้น ไม่ผูกกับบัญชี Claude หรือ Codex และไม่ได้ตรวจสอบบัญชีเหล่านั้น การแยกโทเค็นตามผู้ช่วยมีประโยชน์เฉพาะเมื่อต้องการเพิกถอนหรือติดตามแยกกัน",
  "A token above was issued before tokens were tied to a workspace, and no longer works anywhere. It cannot be repaired — only a hash of it was ever stored — so mint a replacement here, register your checkouts with it, and revoke the old one.":
    "มีโทเค็นด้านบนที่ออกก่อนระบบจะผูกโทเค็นกับเวิร์กสเปซ จึงใช้ไม่ได้อีกแล้ว และแก้ไขไม่ได้เพราะระบบเก็บไว้เพียงค่า hash ให้สร้างโทเค็นใหม่ที่นี่ ใช้ลงทะเบียน checkout ของคุณ แล้วเพิกถอนโทเค็นเดิม",
  "Mint a token": "สร้างโทเค็น",
  "Where it will live, so you can tell which to revoke later. The machine is the useful part, not the assistant.":
    "ระบุว่าจะใช้โทเค็นนี้ที่ไหน เพื่อให้รู้ภายหลังว่าจะเพิกถอนอันไหน ชื่อเครื่องมีประโยชน์กว่าชื่อผู้ช่วย",
  "Work laptop": "โน้ตบุ๊กที่ทำงาน",
  "Days until it expires": "จำนวนวันก่อนหมดอายุ",
  "Between 1 and 365.": "ระหว่าง 1 ถึง 365",
  Mint: "สร้าง",
  "Copy this now. It is shown once and stored only as a hash, so nobody — including this page — can show it to you again.":
    "คัดลอกตอนนี้ โทเค็นแสดงเพียงครั้งเดียว และระบบเก็บไว้เพียงค่า hash จึงไม่มีใคร รวมถึงหน้านี้ แสดงให้คุณเห็นได้อีก",
  "Expires {when}. Register a checkout with it using the command below: the devbuddy client asks for the token once per workspace and keeps it in your operating system's credential store. Never put it in a configuration file.":
    "หมดอายุ {when} ใช้คำสั่งด้านล่างลงทะเบียน checkout โปรแกรม devbuddy จะขอโทเค็นครั้งเดียวต่อเวิร์กสเปซ และเก็บไว้ในที่เก็บรหัสผ่านของระบบปฏิบัติการ ห้ามใส่โทเค็นในไฟล์ตั้งค่าใดๆ",
  "It works in this workspace only. A session is one workspace, the one the checkout it was started in is registered to, and changing folder afterwards does not change it.":
    "โทเค็นนี้ใช้ได้ในเวิร์กสเปซนี้เท่านั้น หนึ่งเซสชันคือหนึ่งเวิร์กสเปซ คือเวิร์กสเปซที่ checkout ซึ่งเปิดเซสชันลงทะเบียนไว้ การเปลี่ยนโฟลเดอร์ภายหลังไม่ได้เปลี่ยนเวิร์กสเปซ",
  "Connect a checkout": "เชื่อมต่อ checkout",
  "In the checkout, on the machine where Claude Code or Codex runs, with the devbuddy client installed:":
    "รันใน checkout บนเครื่องที่ใช้ Claude Code หรือ Codex ซึ่งติดตั้งโปรแกรม devbuddy ไว้แล้ว:",
  "Add a project to make it the assistants' default. A project closed to AI is not listed to them at all.":
    "ระบุโปรเจกต์เพื่อให้ผู้ช่วยใช้เป็นค่าเริ่มต้นได้ โปรเจกต์ที่ปิดการเข้าถึงของ AI จะไม่แสดงให้ผู้ช่วยเห็นเลย",
  Command: "คำสั่ง",
  "Then run devbuddy doctor there. It checks the registration, the stored token, this server's certificate and that the token works in this workspace.":
    "จากนั้นรัน devbuddy doctor ใน checkout นั้น เพื่อตรวจการลงทะเบียน โทเค็นที่เก็บไว้ ใบรับรองของเซิร์ฟเวอร์นี้ และตรวจว่าโทเค็นใช้ได้ในเวิร์กสเปซนี้",

  // Projects
  Projects: "โปรเจกต์",
  "No projects yet.": "ยังไม่มีโปรเจกต์",
  "Create one below.": "สร้างได้ด้านล่าง",
  "An administrator has to create one.": "ผู้ดูแลระบบต้องเป็นผู้สร้าง",
  "AI access": "การเข้าถึงของ AI",
  Enabled: "เปิดใช้",
  Denied: "ปฏิเสธ",
  "Approved before rules could be named: every personal-data rule is off for AI.":
    "อนุมัติไว้ก่อนระบบจะระบุกฎได้ กฎข้อมูลส่วนบุคคลทุกข้อจึงปิดสำหรับ AI",
  "Bounded scope of {name}": "ขอบเขตข้อมูลที่อนุญาตของ {name}",
  "AI may see: {rules}": "AI มองเห็นได้: {rules}",
  "New project": "โปรเจกต์ใหม่",
  "Payments platform": "แพลตฟอร์มชำระเงิน",
  Create: "สร้าง",
  "A new project is closed to AI until somebody opens it, and stays closed if nobody does.":
    "โปรเจกต์ใหม่จะปิดไม่ให้ AI เข้าถึงจนกว่าจะมีผู้เปิดให้ และจะปิดอยู่ต่อไปหากไม่มีใครเปิด",
  Delete: "ลบ",
  "Type {name} to confirm deletion": "พิมพ์ {name} เพื่อยืนยันการลบ",
  "Delete for good": "ลบถาวร",
  "Records, history, and evidence go with it. Audit history stays.":
    "บันทึก ประวัติ และหลักฐานจะถูกลบไปด้วย ส่วนประวัติการตรวจสอบย้อนหลังยังคงอยู่",
  "Email addresses": "ที่อยู่อีเมล",
  "US social security numbers": "หมายเลขประกันสังคมสหรัฐฯ",
  "Payment card numbers": "หมายเลขบัตรชำระเงิน",
  "Thai national ID numbers": "เลขประจำตัวประชาชนไทย",
  "Thai mobile numbers": "หมายเลขโทรศัพท์มือถือไทย",
  "Labelled personal details (date of birth, ID, address)": "ข้อมูลส่วนบุคคลที่มีป้ายกำกับ (วันเกิด เลขประจำตัว ที่อยู่)",
  "Bounded scope": "ขอบเขตข้อมูลที่อนุญาต",
  "Bounded scope for {name}": "ขอบเขตข้อมูลที่อนุญาตสำหรับ {name}",
  "Choose what an assistant may see on {name}. Everything not ticked stays blocked and redacted.":
    "เลือกข้อมูลที่ผู้ช่วย AI มองเห็นได้ใน {name} รายการที่ไม่ได้เลือกจะยังถูกบล็อกและปกปิด",
  "Why is this approved?": "เหตุผลที่อนุมัติ",
  "I approve an assistant seeing: {labels}. Secrets stay refused.":
    "ฉันอนุมัติให้ผู้ช่วย AI มองเห็น: {labels} ข้อมูลลับยังคงถูกปฏิเสธ",
  "Approve scope": "อนุมัติขอบเขต",
  "Withdraw scope": "ถอนขอบเขต",
  "Deny AI access": "ปฏิเสธการเข้าถึงของ AI",
  "Enable AI access": "เปิดให้ AI เข้าถึง",

  // Record detail
  "Back to records": "กลับไปที่บันทึก",
  "Published content": "เนื้อหาที่เผยแพร่",
  "Current content": "เนื้อหาปัจจุบัน",
  "Revision {revision} — not published": "ฉบับแก้ไขที่ {revision} — ยังไม่เผยแพร่",
  History: "ประวัติ",
  "Revision {number} — {title}": "ฉบับแก้ไขที่ {number} — {title}",
  Published: "เผยแพร่แล้ว",
  "· {count} evidence item(s)": "· หลักฐาน {count} รายการ",
  "Approved {when} by {approver} — who also wrote the draft":
    "อนุมัติเมื่อ {when} โดย {approver} — ซึ่งเป็นผู้เขียนฉบับร่างด้วย",
  "Approved {when} by {approver}": "อนุมัติเมื่อ {when} โดย {approver}",
  "No approval covers this revision.": "ยังไม่มีการอนุมัติที่ครอบคลุมฉบับแก้ไขนี้",
  "Sent back {when} by {requester}: {reason}": "ส่งกลับเมื่อ {when} โดย {requester}: {reason}",
  "Revision {revisionNumber}": "ฉบับแก้ไขที่ {revisionNumber}",
  "· never published": "· ยังไม่เคยเผยแพร่",
  "· published": "· เผยแพร่แล้ว",
  "· not published, readers see revision {revision}": "· ยังไม่เผยแพร่ ผู้อ่านเห็นฉบับแก้ไขที่ {revision}",
  "· drafted by AI": "· ร่างโดย AI",
  "· from {source} · recorded by {author}": "· จาก {source} · บันทึกโดย {author}",
  "AI marking": "การระบุว่าเขียนโดย AI",
  "Marked as written by AI by {markedBy} on {when}: {reason}":
    "{markedBy} ระบุว่าเขียนโดย AI เมื่อ {when}: {reason}",
  "Front matter": "ข้อมูลส่วนหัว",
  "Evidence: {description}": "หลักฐาน: {description}",
  "Revise this draft": "แก้ไขฉบับร่างนี้",
  "Sent back": "ส่งกลับ",
  "Edit this draft": "แก้ไขฉบับร่าง",
  "Editing revision {current}. Saving adds revision {next}; this one stays in the history as it is.":
    "กำลังแก้ไขฉบับแก้ไขที่ {current} เมื่อบันทึกจะได้ฉบับแก้ไขที่ {next} ส่วนฉบับนี้ยังอยู่ในประวัติตามเดิม",
  Title: "ชื่อเรื่อง",
  Body: "เนื้อหา",
  "Field {number} name": "ชื่อฟิลด์ที่ {number}",
  "Field {number} value": "ค่าของฟิลด์ที่ {number}",
  "Remove field {number}": "ลบฟิลด์ที่ {number}",
  Remove: "ลบ",
  "Add a field": "เพิ่มฟิลด์",
  "The field “{duplicated}” is named twice.": "มีฟิลด์ชื่อ “{duplicated}” ซ้ำกัน",
  "Evidence, kept with the new revision": "หลักฐานที่เก็บไว้กับฉบับแก้ไขใหม่",
  "Save as a new revision": "บันทึกเป็นฉบับแก้ไขใหม่",
  "Submit for approval": "ส่งขออนุมัติ",
  "Revision {number} is a draft, and nobody is asked to review a draft. Submitting it puts it in the review queue, where a reviewer approves this exact content or sends it back. Submitting publishes nothing.":
    "ฉบับแก้ไขที่ {number} เป็นฉบับร่าง และฉบับร่างจะไม่ถูกส่งให้ใครตรวจทาน การส่งจะนำเข้าคิวตรวจทาน ซึ่งผู้ตรวจทานจะอนุมัติเนื้อหานี้ตามที่เป็นอยู่หรือส่งกลับ การส่งไม่ได้เผยแพร่สิ่งใด",
  "Approve or send back": "อนุมัติหรือส่งกลับ",
  "You are approving {revision}, and nothing else. The approval binds to this exact content:":
    "คุณกำลังอนุมัติ{revision}เท่านั้น การอนุมัติผูกกับเนื้อหานี้ตามที่เป็นอยู่:",
  "revision {number}": "ฉบับแก้ไขที่ {number}",
  "Approve revision {number}": "อนุมัติฉบับแก้ไขที่ {number}",
  "Or send it back": "หรือส่งกลับ",
  "The reason is kept on the record and shown to whoever revises it.":
    "เหตุผลจะเก็บไว้กับบันทึก และแสดงให้ผู้ที่แก้ไขเห็น",
  "Request a correction": "ขอให้แก้ไข",
  Publish: "เผยแพร่",
  "Publishing makes the approved revision the one readers see. An approval that no longer covers the current content is refused.":
    "การเผยแพร่ทำให้ผู้อ่านเห็นฉบับแก้ไขที่อนุมัติแล้ว หากการอนุมัติไม่ครอบคลุมเนื้อหาปัจจุบันแล้ว การเผยแพร่จะถูกปฏิเสธ",
  "Record as written by AI": "ระบุว่าบันทึกนี้เขียนโดย AI",
  "For a record an assistant wrote that was stored as a person's work. Every revision is marked, with your name and this reason beside it. The mark cannot be removed. The content and its approval are not changed.":
    "สำหรับบันทึกที่ผู้ช่วย AI เขียนแต่ถูกเก็บเป็นผลงานของบุคคล ทุกฉบับแก้ไขจะถูกระบุ พร้อมชื่อของคุณและเหตุผลนี้ การระบุนี้ลบไม่ได้ และไม่เปลี่ยนเนื้อหาหรือการอนุมัติ",
  "How do you know an AI wrote it?": "คุณรู้ได้อย่างไรว่า AI เป็นผู้เขียน",
  "Mark as written by AI": "ระบุว่าเขียนโดย AI",
  Archive: "เก็บถาวร",
  "Archiving takes this record out of use. An archived record cannot be revised, approved, or published again, and there is no way to bring it back. Its history stays readable.":
    "การเก็บถาวรจะนำบันทึกนี้ออกจากการใช้งาน บันทึกที่เก็บถาวรแล้วแก้ไข อนุมัติ หรือเผยแพร่อีกไม่ได้ และนำกลับมาไม่ได้ ประวัติยังเปิดอ่านได้",
  "Archive for good": "เก็บถาวรถาวร",

  // Records
  Draft: "ฉบับร่าง",
  "Waiting for approval": "รออนุมัติ",
  Approved: "อนุมัติแล้ว",
  Archived: "เก็บถาวรแล้ว",
  "Knowledge records": "บันทึกความรู้",
  "A new draft is written from its work item, because every record belongs to one.":
    "ฉบับร่างใหม่เขียนจากหน้างาน เพราะทุกบันทึกต้องอยู่ภายใต้งานใดงานหนึ่ง",
  Records: "บันทึก",
  Status: "สถานะ",
  "Every status": "ทุกสถานะ",
  "Nothing is waiting for approval.": "ไม่มีรายการที่รออนุมัติ",
  "No records match that filter.": "ไม่มีบันทึกที่ตรงกับตัวกรองนี้",
  Kind: "ชนิด",
  Revision: "ฉบับแก้ไข",
  Updated: "อัปเดตเมื่อ",
  "(nothing published)": "(ยังไม่มีฉบับเผยแพร่)",
  "(published: {revision})": "(เผยแพร่: {revision})",

  // Search
  Search: "ค้นหา",
  Ask: "ค้นหา",
  "Question or words": "คำถามหรือคำค้น",
  "Kinds (full-text only)": "ชนิด (เฉพาะค้นหาข้อความ)",
  "None ticked means every kind.": "ถ้าไม่เลือก จะค้นทุกชนิด",
  "Statuses (full-text only)": "สถานะ (เฉพาะค้นหาข้อความ)",
  "None ticked means every status, drafts included.": "ถ้าไม่เลือก จะค้นทุกสถานะ รวมถึงฉบับร่าง",
  "At most": "ไม่เกิน",
  "Search the text": "ค้นหาข้อความ",
  "Search by meaning": "ค้นหาตามความหมาย",
  "Full-text results": "ผลการค้นหาข้อความ",
  "Nothing matched.": "ไม่พบรายการที่ตรงกัน",
  Excerpt: "ข้อความที่ตรงกัน",
  "Results by meaning": "ผลการค้นหาตามความหมาย",
  "Nothing close enough.": "ไม่พบรายการที่ใกล้เคียงพอ",
  Distance: "ระยะห่าง",

  // Sign in and passwords
  "The two passwords do not match.": "รหัสผ่านทั้งสองช่องไม่ตรงกัน",
  "The server could not be reached.": "ติดต่อเซิร์ฟเวอร์ไม่ได้",
  ADMINISTRATION: "ระบบจัดการ",
  "Set a password": "ตั้งรหัสผ่าน",
  "Your password is set and every other session has been signed out.":
    "ตั้งรหัสผ่านแล้ว และออกจากระบบในเซสชันอื่นทั้งหมดแล้ว",
  "Sign in": "เข้าสู่ระบบ",
  Token: "โทเค็น",
  "The setup or recovery token you were given.": "โทเค็นตั้งค่าหรือโทเค็นกู้คืนบัญชีที่คุณได้รับ",
  "New password": "รหัสผ่านใหม่",
  "At least twelve characters. Length is the only rule.": "อย่างน้อย 12 ตัวอักษร ความยาวเป็นกฎข้อเดียว",
  "Confirm password": "ยืนยันรหัสผ่าน",
  "Set password": "ตั้งรหัสผ่าน",
  "Recover your account": "กู้คืนบัญชี",
  "If that address has an account, a recovery token has been issued. It is delivered out of band — ask whoever runs this installation for it, then use the link below.":
    "หากอีเมลนี้มีบัญชีอยู่ ระบบได้ออกโทเค็นกู้คืนแล้ว โทเค็นจะส่งผ่านช่องทางอื่น ให้ขอจากผู้ดูแลระบบนี้ แล้วใช้ลิงก์ด้านล่าง",
  Password: "รหัสผ่าน",
  "Send a recovery token": "ส่งโทเค็นกู้คืนบัญชี",
  "Forgot your password?": "ลืมรหัสผ่าน?",
  "Back to sign in": "กลับไปหน้าเข้าสู่ระบบ",
  "Have a setup or recovery token?": "มีโทเค็นตั้งค่าหรือโทเค็นกู้คืนบัญชี?",

  // Teams
  "Teams in {name}": "ทีมใน {name}",
  "Teams in this workspace": "ทีมในเวิร์กสเปซนี้",
  "No teams yet. Create one below.": "ยังไม่มีทีม สร้างได้ด้านล่าง",
  Team: "ทีม",
  Members: "สมาชิก",
  "A team is a grouping and nothing more. It carries no role and no permission — membership decides what anybody may do, whether or not they are on a team.":
    "ทีมเป็นเพียงการจัดกลุ่ม ไม่มีบทบาทหรือสิทธิ์ในตัวเอง สิ่งที่แต่ละคนทำได้ขึ้นกับการเป็นสมาชิกเวิร์กสเปซ ไม่ว่าจะอยู่ในทีมหรือไม่",
  "New team": "ทีมใหม่",
  Platform: "แพลตฟอร์ม",
  "New name for {name}": "ชื่อใหม่ของ {name}",
  Save: "บันทึก",
  "Members shown below": "แสดงสมาชิกด้านล่าง",
  "Delete this team?": "ลบทีมนี้?",
  Confirm: "ยืนยัน",
  "Hide members": "ซ่อนสมาชิก",
  Rename: "เปลี่ยนชื่อ",
  "Members of {teamName}": "สมาชิกของ {teamName}",
  "Nobody is on this team yet.": "ยังไม่มีใครอยู่ในทีมนี้",
  "Add somebody to {team}": "เพิ่มผู้ใช้เข้าทีม {team}",
  "Choose a member of this workspace…": "เลือกสมาชิกของเวิร์กสเปซนี้…",
  Add: "เพิ่ม",
  "Everybody with a grant on this workspace is already on this team. Somebody has to have an account and a membership before they can join one.":
    "ทุกคนที่มีสิทธิ์ในเวิร์กสเปซนี้อยู่ในทีมนี้แล้ว ผู้ใช้ต้องมีบัญชีและเป็นสมาชิกก่อนจึงจะเข้าทีมได้",

  // Work item
  "Work item": "งาน",
  "The work": "รายละเอียดงาน",
  Goal: "เป้าหมาย",
  "In scope": "อยู่ในขอบเขต",
  "Deliberately excluded": "ตั้งใจไม่รวมไว้",
  Stakeholders: "ผู้มีส่วนได้ส่วนเสีย",
  "Knowledge written for this work": "ความรู้ที่เขียนไว้สำหรับงานนี้",
  "Nothing written yet.": "ยังไม่มีการเขียน",
  "Write a new draft": "เขียนฉบับร่างใหม่",
  "A draft is not knowledge yet. Nobody reading published knowledge sees it until it has been submitted, approved and published.":
    "ฉบับร่างยังไม่ถือเป็นความรู้ ผู้อ่านความรู้ที่เผยแพร่จะไม่เห็นจนกว่าจะส่ง อนุมัติ และเผยแพร่แล้ว",
  "Where it came from": "ที่มา",
  Source: "แหล่งที่มา",
  "Where a later reader can check this: a meeting, a document, a commit, a ticket.":
    "จุดที่ผู้อ่านภายหลังตรวจสอบได้ เช่น การประชุม เอกสาร commit หรือ ticket",
  "Markdown. The context, the conclusion, and why.": "เขียนด้วย Markdown ระบุบริบท ข้อสรุป และเหตุผล",
  "This project holds no evidence yet.": "โปรเจกต์นี้ยังไม่มีหลักฐาน",
  "What evidence {evidence} shows": "หลักฐาน {evidence} แสดงอะไร",
  "What this shows": "สิ่งที่หลักฐานนี้แสดง",
  "Save draft": "บันทึกฉบับร่าง",
  "Handing this work over": "การส่งมอบงานนี้",
  "Generate a handover": "สร้างเอกสารส่งมอบงาน",
  "Find open questions": "หาคำถามที่ยังค้างอยู่",
  "Find missing evidence": "หาหลักฐานที่ขาด",
  "Open questions": "คำถามที่ยังค้างอยู่",
  "No open questions found.": "ไม่พบคำถามที่ค้างอยู่",
  "Missing evidence": "หลักฐานที่ขาด",
  "No gaps in the evidence found.": "ไม่พบหลักฐานที่ขาด",
  Generated: "สร้างเมื่อ",
  "No published knowledge to hand over yet.": "ยังไม่มีความรู้ที่เผยแพร่แล้วให้ส่งมอบ",
  "None.": "ไม่มี",

  // Work items
  Develop: "พัฒนา",
  Enhance: "ปรับปรุง",
  "Fix a bug": "แก้บั๊ก",
  "Change request": "คำขอเปลี่ยนแปลง",
  "Code review": "รีวิวโค้ด",
  Work: "งาน",
  "Nothing registered yet.": "ยังไม่มีงานที่ลงทะเบียน",
  Key: "รหัสงาน",
  Created: "สร้างเมื่อ",
  "Register work": "ลงทะเบียนงาน",
  "What the team says out loud, such as DEV-101.": "รหัสที่ทีมใช้เรียกกัน เช่น DEV-101",
  "What this work is for. The part nobody writes down.": "งานนี้ทำเพื่ออะไร ส่วนที่มักไม่มีใครจดไว้",
  "Usually what a later owner needs and never finds written down.":
    "มักเป็นสิ่งที่ผู้รับช่วงต่อต้องการ แต่ไม่เคยพบว่ามีใครจดไว้",
  Register: "ลงทะเบียน",

  // Workspaces
  "Workspaces you can reach": "เวิร์กสเปซที่คุณเข้าถึงได้",
  Workspace: "เวิร์กสเปซ",
  "You are here": "คุณอยู่ที่นี่",
  "Every workspace is a separate tenant. Nothing is shared between them — not projects, not records, not evidence — and a grant on one says nothing about any other.":
    "แต่ละเวิร์กสเปซแยกจากกันโดยสมบูรณ์ ไม่มีการใช้โปรเจกต์ บันทึก หรือหลักฐานร่วมกัน และสิทธิ์ในเวิร์กสเปซหนึ่งไม่มีผลกับเวิร์กสเปซอื่น",
  "New workspace": "เวิร์กสเปซใหม่",
  Northwind: "นอร์ทวินด์",
  "First project": "โปรเจกต์แรก",
  "Optional. One can be created inside it afterwards instead.": "ไม่บังคับ สร้างภายหลังในเวิร์กสเปซนั้นได้",
  "Create workspace": "สร้างเวิร์กสเปซ",
  "Created, sponsored by {sponsor}. You administer it — {link}.":
    "สร้างแล้ว โดยมี {sponsor} เป็นผู้รับรอง คุณเป็นผู้ดูแลเวิร์กสเปซนี้ — {link}",
  "this workspace": "เวิร์กสเปซนี้",
  "open {name}": "เปิด {name}",
  "Sponsored by {sponsor}: the permission that allows this is the one you hold here, and you become the new workspace's administrator. Nobody else is carried over.":
    "รับรองโดย {sponsor}: สิทธิ์ที่ใช้ทำรายการนี้คือสิทธิ์ที่คุณมีในเวิร์กสเปซนี้ และคุณจะเป็นผู้ดูแลเวิร์กสเปซใหม่ ไม่มีสมาชิกคนอื่นถูกย้ายไปด้วย",
  Workspaces: "เวิร์กสเปซ",
  "Sign out": "ออกจากระบบ",
  "Where you have access": "เวิร์กสเปซที่คุณเข้าถึงได้",
  "Your account is not a member of any workspace. An administrator has to grant you one; there is nothing you can do from here.":
    "บัญชีของคุณยังไม่เป็นสมาชิกของเวิร์กสเปซใด ผู้ดูแลระบบต้องให้สิทธิ์ก่อน ขณะนี้คุณยังทำอะไรจากหน้านี้ไม่ได้",

  // Tours and the ? hints (guide/content.ts, components/Guide.tsx)
  "That is this screen":
    "จบทัวร์ของหน้านี้แล้ว",
  "Every screen has its own tour under the same button. A ? beside a heading or a field explains that one part in more detail.":
    "ทุกหน้ามีทัวร์ของตัวเองที่ปุ่มเดียวกันนี้ ส่วนปุ่ม ? ข้างหัวข้อหรือช่องกรอก จะอธิบายส่วนนั้นอย่างละเอียด",
  "AI access to a project":
    "การเข้าถึงโปรเจกต์ของ AI",
  "Whether Claude, Codex or Cowork may read this project through the DevBuddy plugin. It is denied by default, and a project nobody opened stays closed.\n\nEnabled, an assistant works as the person whose machine token it uses, with exactly that person's role and no more. It can search, read, analyse, write a draft and prepare a handover. It cannot approve, publish, archive, attach evidence or manage anybody: those stay with people, on these screens.\n\nDenied, the project is not even listed to an assistant, and every call naming it is refused. Denying takes effect on the next call. It cannot recall what an assistant already read.":
    "กำหนดว่า Claude, Codex หรือ Cowork อ่านโปรเจกต์นี้ผ่านปลั๊กอิน DevBuddy ได้หรือไม่ ค่าเริ่มต้นคือปฏิเสธ และโปรเจกต์ที่ไม่มีใครเปิดจะปิดอยู่ตลอด\n\nเมื่อเปิด ผู้ช่วย AI จะทำงานในนามเจ้าของ machine token ที่ใช้ ด้วยบทบาทของคนนั้นพอดี ไม่มากกว่า ทำได้แค่ค้นหา อ่าน วิเคราะห์ เขียนฉบับร่าง และเตรียมการส่งมอบงาน ส่วนการอนุมัติ เผยแพร่ เก็บถาวร แนบหลักฐาน และการจัดการคน ยังเป็นหน้าที่ของคนบนหน้าจอเหล่านี้\n\nเมื่อปฏิเสธ ผู้ช่วย AI จะไม่เห็นโปรเจกต์นี้ในรายการเลย และทุกคำขอที่อ้างถึงโปรเจกต์นี้จะถูกปฏิเสธ มีผลตั้งแต่คำขอถัดไป แต่เรียกคืนสิ่งที่ผู้ช่วย AI อ่านไปแล้วไม่ได้",
  "Bounded scope for personal data":
    "ขอบเขตข้อมูลส่วนบุคคลที่อนุญาต",
  "On the AI channel, personal data is blocked from drafts and redacted from what an assistant reads: email addresses, ID and card numbers, Thai mobile numbers and labelled personal details. People are never subject to this rule.\n\nA bounded scope names the kinds an assistant may see on this one project, with the reason it is approved. Everything not ticked stays blocked and redacted. You can withdraw the scope at any time.\n\nSecrets — passwords, tokens, keys, connection strings — are refused whatever the scope says. There is no setting that lets one through.":
    "ในช่องทาง AI ข้อมูลส่วนบุคคลจะถูกบล็อกไม่ให้เข้าไปในฉบับร่าง และถูกปกปิดจากสิ่งที่ผู้ช่วย AI อ่าน ได้แก่ อีเมล เลขประจำตัว เลขบัตร เบอร์มือถือไทย และข้อมูลส่วนบุคคลที่มีป้ายกำกับ กฎนี้ไม่บังคับกับคน\n\nขอบเขตข้อมูลที่อนุญาตระบุว่าผู้ช่วย AI เห็นข้อมูลประเภทใดได้บ้างในโปรเจกต์นี้เท่านั้น พร้อมเหตุผลที่อนุมัติ ประเภทที่ไม่ได้ติ๊กยังถูกบล็อกและปกปิดเหมือนเดิม และถอนขอบเขตได้ทุกเมื่อ\n\nข้อมูลลับ เช่น รหัสผ่าน โทเค็น คีย์ และ connection string ถูกปฏิเสธเสมอไม่ว่าขอบเขตจะว่าอย่างไร ไม่มีการตั้งค่าใดที่ปล่อยให้ผ่าน",
  "Deleting a project":
    "การลบโปรเจกต์",
  "Deleting removes the project's work items, every record with its whole revision history, its evidence files and its project-level grants, at once. There is no undo, and a later restore of an older backup deletes it again.\n\nThat is why you type the project's name back: a confirmation that can be clicked through by habit is not a confirmation.\n\nThe audit history survives. Deleting content is not the same as erasing that the deletion happened.":
    "การลบจะเอางานของโปรเจกต์ บันทึกทุกรายการพร้อมประวัติฉบับแก้ไขทั้งหมด ไฟล์หลักฐาน และสิทธิ์ระดับโปรเจกต์ออกทันที ย้อนกลับไม่ได้ และถ้ากู้คืนจากข้อมูลสำรองที่เก่ากว่าในภายหลัง ระบบจะลบซ้ำอีกครั้ง\n\nจึงต้องพิมพ์ชื่อโปรเจกต์ยืนยัน เพราะการยืนยันที่กดผ่านได้ด้วยความเคยชินไม่ใช่การยืนยันจริง\n\nประวัติการตรวจสอบย้อนหลังยังอยู่ การลบเนื้อหาไม่ได้แปลว่าลบร่องรอยว่าเคยลบ",
  "Roles":
    "บทบาท",
  "Viewer reads the knowledge, work items and evidence in reach, and can mint their own plugin tokens.\n\nContributor can also run analyses, register work, write and revise drafts, submit them for approval and attach evidence.\n\nReviewer can also approve a revision or send it back, publish and archive.\n\nAdministrator can also manage projects, people, teams, sources and AI access, read the audit trail, run maintenance and back up.\n\nIndex maintainer is for a worker account: it reads and keeps the search index, and nothing else. The first four are cumulative; this one is not above Administrator.":
    "ผู้ดู อ่านความรู้ งาน และหลักฐานที่เข้าถึงได้ และสร้างโทเค็นปลั๊กอินของตัวเองได้\n\nผู้ร่วมเขียน ทำได้เพิ่มคือ วิเคราะห์ ลงทะเบียนงาน เขียนและแก้ไขฉบับร่าง ส่งให้อนุมัติ และแนบหลักฐาน\n\nผู้ตรวจทาน ทำได้เพิ่มคือ อนุมัติฉบับแก้ไขหรือส่งกลับ เผยแพร่ และเก็บถาวร\n\nผู้ดูแลระบบ ทำได้เพิ่มคือ จัดการโปรเจกต์ คน ทีม แหล่งข้อมูล และการเข้าถึงของ AI อ่านการตรวจสอบย้อนหลัง ทำงานบำรุงรักษา และสำรองข้อมูล\n\nผู้ดูแลดัชนี มีไว้สำหรับบัญชีของ worker อ่านและดูแลดัชนีค้นหาได้อย่างเดียว สี่บทบาทแรกสะสมต่อกัน ส่วนบทบาทนี้ไม่ได้อยู่เหนือผู้ดูแลระบบ",
  "Where a grant applies":
    "สิทธิ์มีผลที่ไหน",
  "A grant on the whole workspace covers every project in it, including ones created later. A grant on one project covers that project only.\n\nSomebody can hold several grants, such as Viewer on the workspace and Reviewer on one project. Their permission on a project is the widest grant that covers it.":
    "สิทธิ์ระดับทั้งเวิร์กสเปซครอบคลุมทุกโปรเจกต์ในนั้น รวมถึงโปรเจกต์ที่สร้างภายหลัง ส่วนสิทธิ์ระดับโปรเจกต์ครอบคลุมเฉพาะโปรเจกต์นั้น\n\nคนหนึ่งถือสิทธิ์ได้หลายรายการ เช่น ผู้ดูทั้งเวิร์กสเปซ และผู้ตรวจทานในโปรเจกต์เดียว สิ่งที่ทำได้ในโปรเจกต์หนึ่งคือสิทธิ์ที่กว้างที่สุดที่ครอบคลุมโปรเจกต์นั้น",
  "Changing somebody's role":
    "การเปลี่ยนบทบาทของใครสักคน",
  "A grant's role cannot be edited, so a change is two steps: the old grant is revoked, then a new one is made for the same place.\n\nRevoking comes first because it fails safe. If the second step is refused, the person has less access than intended, not more, and the screen tells you to grant it again.\n\nYou cannot change your own grant here: the second step would need the permission the first one took away.":
    "แก้บทบาทของสิทธิ์ที่มีอยู่ไม่ได้ การเปลี่ยนจึงมีสองขั้น คือเพิกถอนสิทธิ์เดิม แล้วให้สิทธิ์ใหม่ในขอบเขตเดิม\n\nเพิกถอนก่อนเพราะปลอดภัยกว่าถ้าล้มเหลว หากขั้นที่สองถูกปฏิเสธ คนนั้นจะมีสิทธิ์น้อยกว่าที่ตั้งใจ ไม่ใช่มากกว่า และหน้าจอจะบอกให้ให้สิทธิ์ใหม่อีกครั้ง\n\nเปลี่ยนสิทธิ์ของตัวเองที่นี่ไม่ได้ เพราะขั้นที่สองต้องใช้สิทธิ์ที่ขั้นแรกเพิ่งเอาออกไป",
  "When access ends":
    "เมื่อสิทธิ์สิ้นสุด",
  "A revoked grant stays in the list, marked Revoked, so the list shows what access somebody had and when it ended.\n\nRevoking takes effect on the person's next request, not their next sign-in. It cannot recall anything they already downloaded: Downloads lists what they downloaded or exported in the last 90 days, so you can follow it up outside the system.":
    "สิทธิ์ที่ถูกเพิกถอนยังอยู่ในรายการโดยมีป้าย เพิกถอนแล้ว เพื่อให้เห็นว่าใครเคยมีสิทธิ์อะไรและสิ้นสุดเมื่อไร\n\nการเพิกถอนมีผลตั้งแต่คำขอถัดไปของคนนั้น ไม่ต้องรอให้เข้าสู่ระบบใหม่ แต่เรียกคืนสิ่งที่ดาวน์โหลดไปแล้วไม่ได้ ปุ่ม การดาวน์โหลด แสดงสิ่งที่คนนั้นดาวน์โหลดหรือส่งออกใน 90 วันที่ผ่านมา เพื่อให้ติดตามต่อนอกระบบได้",
  "Issuing a password reset":
    "การออกโทเค็นรีเซ็ตรหัสผ่าน",
  "For somebody who cannot receive the recovery email: the installation has no mail server, or they lost the address.\n\nThe token is shown once, here, is single-use and expires. Give it to them by a channel you trust; they redeem it on the set-password page, and their other sessions end.\n\nIt is refused if they also belong to a workspace you do not administer, because a password works in every workspace they are in.":
    "สำหรับคนที่รับอีเมลกู้คืนบัญชีไม่ได้ เช่น ระบบไม่มีเมลเซิร์ฟเวอร์ หรือเขาเข้าอีเมลนั้นไม่ได้แล้ว\n\nโทเค็นแสดงครั้งเดียวที่นี่ ใช้ได้ครั้งเดียว และมีวันหมดอายุ ส่งให้เขาผ่านช่องทางที่ไว้ใจได้ เขาใช้โทเค็นในหน้าตั้งรหัสผ่าน แล้ว session อื่นของเขาจะสิ้นสุด\n\nระบบจะปฏิเสธถ้าเขาอยู่ในเวิร์กสเปซที่คุณไม่ได้ดูแลด้วย เพราะรหัสผ่านใช้ได้ทุกเวิร์กสเปซที่เขาอยู่",
  "The setup token":
    "โทเค็นตั้งค่าบัญชี",
  "A new account cannot be signed into until its owner sets a password with this token.\n\nThe token is shown once and is not kept on this page. When the installation has SMTP configured it is also emailed to the address; this page cannot tell whether that delivery worked, so it shows the token either way. It is single-use and expires.":
    "บัญชีใหม่จะยังเข้าสู่ระบบไม่ได้จนกว่าเจ้าของจะตั้งรหัสผ่านด้วยโทเค็นนี้\n\nโทเค็นแสดงครั้งเดียวและหน้านี้ไม่ได้เก็บไว้ ถ้าระบบตั้งค่า SMTP ไว้ จะส่งอีเมลไปที่อยู่นั้นด้วย แต่หน้านี้รู้ไม่ได้ว่าส่งสำเร็จหรือไม่ จึงแสดงโทเค็นไว้เสมอ ใช้ได้ครั้งเดียวและมีวันหมดอายุ",
  "What a team is":
    "ทีมคืออะไร",
  "A team is a named group of people, for knowing who works together. It carries no role and no permission.\n\nWhat anybody may do is decided by their grants on the Members screen, whether or not they are on a team. To add somebody to a team they need an account and a grant on this workspace first.":
    "ทีมคือกลุ่มคนที่ตั้งชื่อไว้ เพื่อให้รู้ว่าใครทำงานด้วยกัน ทีมไม่มีบทบาทและไม่มีสิทธิ์ใดๆ\n\nสิ่งที่ใครทำได้ขึ้นกับสิทธิ์ของเขาในหน้าสมาชิก ไม่ว่าจะอยู่ในทีมหรือไม่ จะเพิ่มใครเข้าทีมได้ คนนั้นต้องมีบัญชีและสิทธิ์ในเวิร์กสเปซนี้ก่อน",
  "Creating another workspace":
    "การสร้างเวิร์กสเปซอีกแห่ง",
  "Each workspace is a separate tenant: projects, records, evidence, members and tokens are never shared between two.\n\nA new workspace is sponsored by this one: you need to administer this workspace to create it, and you become the new one's administrator. There is no installation-wide administrator above workspaces.":
    "แต่ละเวิร์กสเปซแยกขาดจากกัน โปรเจกต์ บันทึก หลักฐาน สมาชิก และโทเค็นไม่ถูกใช้ร่วมกันข้ามเวิร์กสเปซ\n\nเวิร์กสเปซใหม่สร้างโดยมีเวิร์กสเปซนี้เป็นผู้สนับสนุน คุณต้องเป็นผู้ดูแลเวิร์กสเปซนี้จึงจะสร้างได้ และคุณจะเป็นผู้ดูแลของเวิร์กสเปซใหม่ ไม่มีผู้ดูแลระดับทั้งระบบที่อยู่เหนือเวิร์กสเปซ",
  "How the request arrived. Person: the web screens, the API or the console. Assistant: Claude, Codex or Cowork through the plugin. Internal system: the server's own scheduled work, such as the retention sweep and the bootstrap.\n\nAn assistant acts with a person's machine token, so the actor is that person either way. The channel is what tells the two apart.\n\nEntries from before v1.3.0 have no channel. They are shown as not recorded, never guessed.":
    "คำขอเข้ามาทางไหน บุคคล: หน้าเว็บ API หรือ console ผู้ช่วย AI: Claude, Codex หรือ Cowork ผ่านปลั๊กอิน ระบบภายใน: งานตามกำหนดเวลาของเซิร์ฟเวอร์เอง เช่น การกวาดตามอายุข้อมูล และการตั้งค่าเริ่มต้นระบบ\n\nผู้ช่วย AI ทำงานด้วย machine token ของคน ผู้กระทำจึงเป็นคนคนนั้นทั้งสองกรณี ช่องทางคือสิ่งที่แยกสองกรณีออกจากกัน\n\nรายการก่อน v1.3.0 ไม่มีช่องทาง จะแสดงว่าไม่ได้บันทึก และไม่มีการเดา",
  "What an audit entry holds":
    "รายการตรวจสอบย้อนหลังเก็บอะไร",
  "Who did what, to which resource, on which channel, when, and whether it was allowed. Refusals are recorded as well as successes.\n\nAn entry never holds content: no record body, no evidence, no secret. The detail column carries identifiers and counts only, so the audit trail is not a second copy of the knowledge.":
    "ใครทำอะไร กับทรัพยากรไหน ผ่านช่องทางใด เมื่อไร และได้รับอนุญาตหรือไม่ บันทึกทั้งคำขอที่ถูกปฏิเสธและที่สำเร็จ\n\nรายการไม่เก็บเนื้อหาเลย ไม่มีเนื้อหาบันทึก ไม่มีหลักฐาน ไม่มีข้อมูลลับ คอลัมน์รายละเอียดมีเพียงรหัสและจำนวน การตรวจสอบย้อนหลังจึงไม่ใช่สำเนาที่สองของความรู้",
  "Backup and restore":
    "การสำรองและกู้คืนข้อมูล",
  "Back up now writes every workspace — rows and evidence files — to the server's backup volume. Copy it off that volume to keep it safe from losing the disk.\n\nRestoring is not on any screen. It is a console command run by whoever operates the server, because a restore from total loss runs on a database with no accounts left to check.\n\nBackups older than the retention period are removed by the retention sweep.":
    "สำรองข้อมูลตอนนี้ จะเขียนทุกเวิร์กสเปซ ทั้งข้อมูลในฐานข้อมูลและไฟล์หลักฐาน ลงใน volume สำรองของเซิร์ฟเวอร์ ควรคัดลอกออกไปไว้ที่อื่น เพื่อไม่ให้หายไปพร้อมดิสก์\n\nการกู้คืนไม่มีบนหน้าจอใด เป็นคำสั่ง console ที่ผู้ดูแลเซิร์ฟเวอร์รัน เพราะการกู้คืนหลังสูญเสียทั้งหมดทำบนฐานข้อมูลที่ไม่เหลือบัญชีให้ตรวจสิทธิ์\n\nข้อมูลสำรองที่เก่ากว่าระยะเวลาเก็บรักษาจะถูกลบโดยการกวาดตามอายุข้อมูล",
  "Machine tokens":
    "Machine token",
  "What the devbuddy client gives Claude Code, Codex or Cowork, so the assistant can call DevBuddy as you.\n\nIt carries your permissions and no more, in this workspace only. Revoking it stops the next call. Only a hash is stored, so the value is shown once; a lost token is replaced, never recovered.\n\nThe client keeps it in the operating system's credential store, per registered folder, and hands it only to the server that folder is registered to.":
    "สิ่งที่โปรแกรม devbuddy ส่งให้ Claude Code, Codex หรือ Cowork เพื่อให้ผู้ช่วย AI เรียก DevBuddy ในนามคุณ\n\nโทเค็นมีสิทธิ์เท่าคุณ ไม่มากกว่า และใช้ได้เฉพาะเวิร์กสเปซนี้ เพิกถอนแล้วคำขอถัดไปจะหยุดทันที ระบบเก็บไว้แค่ค่า hash จึงแสดงค่าจริงครั้งเดียว โทเค็นที่หายต้องออกใหม่ กู้คืนไม่ได้\n\nโปรแกรมเก็บโทเค็นไว้ในที่เก็บรหัสของระบบปฏิบัติการ แยกตามโฟลเดอร์ที่ลงทะเบียน และส่งให้เฉพาะเซิร์ฟเวอร์ที่โฟลเดอร์นั้นลงทะเบียนไว้",
  "Connecting a folder":
    "การเชื่อมโฟลเดอร์",
  "The command ties a folder on your machine to this server and this workspace. Run it in the folder where you start Claude Code or Codex, then store the token with devbuddy token set.\n\nAdding --project makes that project the assistant's default. A session uses the workspace of the folder it was started in, and changing folder later does not change it.\n\ndevbuddy doctor checks the registration, the stored token, the server's certificate and that the token works here.":
    "คำสั่งนี้ผูกโฟลเดอร์ในเครื่องของคุณกับเซิร์ฟเวอร์และเวิร์กสเปซนี้ รันในโฟลเดอร์ที่คุณเปิด Claude Code หรือ Codex แล้วเก็บโทเค็นด้วย devbuddy token set\n\nเติม --project เพื่อให้โปรเจกต์นั้นเป็นค่าเริ่มต้นของผู้ช่วย AI แต่ละ session ใช้เวิร์กสเปซของโฟลเดอร์ที่เปิด session และการเปลี่ยนโฟลเดอร์ภายหลังไม่ได้เปลี่ยนตาม\n\ndevbuddy doctor ตรวจการลงทะเบียน โทเค็นที่เก็บไว้ ใบรับรองของเซิร์ฟเวอร์ และว่าโทเค็นใช้งานได้ที่นี่",
  "Types of work":
    "ประเภทงาน",
  "Develop: new work. Enhance: improving something that exists. Fix a bug: a defect, its cause and the fix.\n\nChange request and Code review are separate types and are never abbreviated to one name. A change request changes agreed scope; a code review is feedback on code and what was done about it.":
    "พัฒนา: งานใหม่ ปรับปรุง: ทำสิ่งที่มีอยู่ให้ดีขึ้น แก้บั๊ก: ข้อบกพร่อง สาเหตุ และวิธีแก้\n\nคำขอเปลี่ยนแปลง กับ รีวิวโค้ด เป็นคนละประเภทกัน และไม่ย่อรวมเป็นชื่อเดียว คำขอเปลี่ยนแปลงคือการเปลี่ยนขอบเขตที่ตกลงกันไว้ ส่วนรีวิวโค้ดคือความเห็นต่อโค้ดและสิ่งที่ทำต่อความเห็นนั้น",
  "Kinds of record":
    "ประเภทของบันทึก",
  "Decision: what was decided, why, the alternatives and who approved it. Technical knowledge: architecture, data flows, APIs and configuration worth knowing. Change impact: what a change affected, risks, migrations and rollback. Handover: what the next owner needs. Code review feedback: what a review said and what was done about it. Context reference: links to issues, documents and environments. Delivery state: status, acceptance criteria and how it was verified.":
    "การตัดสินใจ: ตัดสินใจอะไร เพราะอะไร มีทางเลือกอื่นอะไร และใครอนุมัติ ความรู้ทางเทคนิค: สถาปัตยกรรม การไหลของข้อมูล API และการตั้งค่าที่ควรรู้ ผลกระทบของการเปลี่ยนแปลง: การเปลี่ยนแปลงกระทบอะไร ความเสี่ยง การย้ายข้อมูล และการย้อนกลับ การส่งมอบงาน: สิ่งที่เจ้าของคนถัดไปต้องรู้ ความเห็นจากการรีวิวโค้ด: รีวิวว่าอย่างไร และทำอะไรต่อ ข้อมูลอ้างอิงบริบท: ลิงก์ไปยัง issue เอกสาร และสภาพแวดล้อม สถานะการส่งมอบ: สถานะ เกณฑ์การยอมรับ และวิธีตรวจสอบ",
  "Where a record's content came from, so a later reader can check it against its origin: a meeting, a document, a commit, a ticket.\n\nWhere it came from says what kind of source it was. Source is where to find it. Your name and the time are recorded for you.\n\nWhether an AI wrote it is not chosen here: the server marks anything an assistant sends through the plugin. Choose Written by an AI assistant when you are pasting what an assistant wrote somewhere else.":
    "เนื้อหาของบันทึกมาจากไหน เพื่อให้คนอ่านในภายหลังตรวจกับต้นทางได้ เช่น การประชุม เอกสาร commit หรือ ticket\n\nช่อง ที่มา บอกว่าต้นทางเป็นแบบไหน ช่อง แหล่งที่มา บอกว่าจะไปหาได้ที่ไหน ชื่อของคุณและเวลาระบบบันทึกให้เอง\n\nไม่ต้องเลือกว่า AI เขียนหรือไม่ เซิร์ฟเวอร์จะทำเครื่องหมายทุกอย่างที่ผู้ช่วย AI ส่งผ่านปลั๊กอินเอง ให้เลือก เขียนโดยผู้ช่วย AI เมื่อคุณวางสิ่งที่ผู้ช่วย AI เขียนไว้ที่อื่น",
  "Named fields kept with the body, such as owner, component or ticket. Each name may appear once.\n\nThey are part of the content an approval binds to, which is why they are shown beside the body: a reviewer approves the fields as well as the text.":
    "ช่องข้อมูลที่ตั้งชื่อไว้และเก็บคู่กับเนื้อหา เช่น เจ้าของ ส่วนประกอบ หรือ ticket แต่ละชื่อใช้ได้ครั้งเดียว\n\nข้อมูลส่วนหัวเป็นส่วนหนึ่งของเนื้อหาที่การอนุมัติผูกไว้ จึงแสดงคู่กับเนื้อหา ผู้ตรวจทานอนุมัติทั้งช่องข้อมูลและข้อความ",
  "Linking evidence to a draft":
    "การเชื่อมหลักฐานกับฉบับร่าง",
  "Tick the evidence that backs this record and say what each shows. Evidence is attached on the project's Evidence screen first; here you only link it.\n\nThe links are kept with every later revision. Find missing evidence on a work item points at claims nothing backs up.":
    "ติ๊กหลักฐานที่สนับสนุนบันทึกนี้ และบอกว่าแต่ละชิ้นแสดงอะไร หลักฐานต้องแนบที่หน้าหลักฐานของโปรเจกต์ก่อน ที่นี่ทำได้แค่เชื่อม\n\nการเชื่อมจะติดไปกับทุกฉบับแก้ไขถัดไป ปุ่ม หาหลักฐานที่ขาด ในหน้างาน จะชี้ข้อความที่ยังไม่มีอะไรรองรับ",
  "Handing work over":
    "การส่งมอบงาน",
  "Generate a handover assembles what is published for this work, kind by kind, with the open questions and missing evidence. Drafts are not included: a draft is not knowledge yet.\n\nFind open questions and Find missing evidence run the two checks on their own. Each reads every record of the work, so they run when you ask rather than on every visit.":
    "สร้างเอกสารส่งมอบงาน จะรวบรวมสิ่งที่เผยแพร่แล้วของงานนี้ทีละประเภท พร้อมคำถามที่ค้างอยู่และหลักฐานที่ขาด ไม่รวมฉบับร่าง เพราะฉบับร่างยังไม่ใช่ความรู้\n\nหาคำถามที่ยังค้างอยู่ และ หาหลักฐานที่ขาด ทำการตรวจสองอย่างนี้แยกกัน แต่ละอย่างอ่านทุกบันทึกของงาน จึงทำเมื่อคุณสั่ง ไม่ใช่ทุกครั้งที่เปิดหน้า",
  "How a record becomes knowledge":
    "บันทึกกลายเป็นความรู้อย่างไร",
  "Draft: written by a person or an assistant, and not knowledge yet. Handovers and search by meaning leave it out, and a text search shows it only when Draft is ticked.\n\nWaiting for approval: submitted. A reviewer approves one exact revision or sends it back with a reason.\n\nApproved: a person agreed to that exact content. Published: readers of published knowledge, search and assistants now see it.\n\nArchived: out of use for good. Its history stays readable.":
    "ฉบับร่าง: เขียนโดยคนหรือผู้ช่วย AI และยังไม่ใช่ความรู้ การส่งมอบงานและการค้นหาตามความหมายจะไม่รวมฉบับร่าง และการค้นหาข้อความจะแสดงเมื่อติ๊ก ฉบับร่าง เท่านั้น\n\nรออนุมัติ: ส่งแล้ว ผู้ตรวจทานอนุมัติฉบับแก้ไขที่ระบุแน่ชัดหนึ่งฉบับ หรือส่งกลับพร้อมเหตุผล\n\nอนุมัติแล้ว: มีคนเห็นชอบกับเนื้อหานั้นพอดี เผยแพร่แล้ว: ผู้อ่านความรู้ที่เผยแพร่ การค้นหา และผู้ช่วย AI เห็นได้แล้ว\n\nเก็บถาวรแล้ว: เลิกใช้ถาวร ประวัติยังอ่านได้",
  "Why the approval names a hash":
    "ทำไมการอนุมัติต้องระบุ hash",
  "The long code is the content hash of the revision on screen: title, body and front matter together.\n\nApproving sends that hash. If anybody saves a new revision while you are reading, the hash no longer matches and the server refuses the approval, so you can never approve content you were not shown. Read the new revision and approve that.\n\nYou may approve a draft you wrote yourself; the history records that you did.":
    "รหัสยาวนั้นคือ content hash ของฉบับแก้ไขที่แสดงอยู่ ซึ่งคำนวณจากชื่อ เนื้อหา และข้อมูลส่วนหัวรวมกัน\n\nการอนุมัติจะส่ง hash นี้ไป ถ้ามีคนบันทึกฉบับแก้ไขใหม่ระหว่างที่คุณอ่าน hash จะไม่ตรงและเซิร์ฟเวอร์จะปฏิเสธการอนุมัติ คุณจึงไม่มีทางอนุมัติเนื้อหาที่ไม่ได้เห็น ให้อ่านฉบับใหม่แล้วอนุมัติฉบับนั้น\n\nอนุมัติฉบับร่างที่ตัวเองเขียนได้ และประวัติจะบันทึกไว้ว่าคุณเป็นคนเขียนเอง",
  "Published content and newer revisions":
    "เนื้อหาที่เผยแพร่ และฉบับแก้ไขที่ใหม่กว่า",
  "Readers see the published revision. A record can be revised after it is published: the newer revision is shown separately, as not published, until it is approved and published in turn.\n\nRevisions are never changed once saved. Editing adds a new revision and keeps the old one in the history.":
    "ผู้อ่านเห็นฉบับแก้ไขที่เผยแพร่ บันทึกแก้ไขต่อได้หลังเผยแพร่ ฉบับที่ใหม่กว่าจะแสดงแยกไว้ว่ายังไม่เผยแพร่ จนกว่าจะได้รับอนุมัติและเผยแพร่ตามขั้นตอน\n\nฉบับแก้ไขที่บันทึกแล้วไม่ถูกเปลี่ยนอีก การแก้ไขจะเพิ่มฉบับใหม่และเก็บฉบับเดิมไว้ในประวัติ",
  "Recording that an AI wrote a record":
    "การบันทึกว่า AI เป็นผู้เขียน",
  "For a record an assistant wrote that was stored as a person's work, which could happen before 15 September 2026, when nothing recorded the channel a draft came from.\n\nEvery revision is marked, with your name, the time and your reason beside the mark. Nothing removes it. The content and its approvals are not changed.":
    "สำหรับบันทึกที่ผู้ช่วย AI เขียน แต่ถูกเก็บเป็นงานของคน ซึ่งเกิดขึ้นได้ก่อนวันที่ 15 กันยายน 2026 ตอนที่ยังไม่มีอะไรบันทึกช่องทางที่ฉบับร่างเข้ามา\n\nทุกฉบับแก้ไขจะถูกทำเครื่องหมาย พร้อมชื่อของคุณ เวลา และเหตุผล ไม่มีอะไรลบเครื่องหมายนี้ได้ เนื้อหาและการอนุมัติไม่เปลี่ยน",
  "Archiving":
    "การเก็บถาวร",
  "Archiving takes a record out of use. It can no longer be revised, approved or published, it leaves semantic search, and nothing brings it back.\n\nIts history stays readable. To replace a record, write a new draft and archive the old one once the new one is published.":
    "การเก็บถาวรคือเลิกใช้บันทึก จะแก้ไข อนุมัติ หรือเผยแพร่อีกไม่ได้ ออกจากการค้นหาตามความหมาย และไม่มีทางนำกลับมา\n\nประวัติยังอ่านได้ ถ้าจะแทนที่บันทึก ให้เขียนฉบับร่างใหม่ แล้วค่อยเก็บถาวรฉบับเก่าเมื่อฉบับใหม่เผยแพร่แล้ว",
  "Evidence state":
    "สถานะของหลักฐาน",
  "Every file is scanned when it is attached, and one that fails the scan is refused rather than kept, so a file attached on this screen is Clean: scanned, nothing found, and it can be downloaded.\n\nRedacted means sensitive parts were removed before it was kept, and it can be downloaded too. Not scanned means no scan was recorded, and it is never released until one is. Blocked is never released.\n\nDownload is offered only for evidence that can be released.":
    "ทุกไฟล์ถูกสแกนตอนแนบ ไฟล์ที่สแกนไม่ผ่านจะถูกปฏิเสธ ไม่ถูกเก็บ ไฟล์ที่แนบจากหน้านี้จึงเป็น Clean คือสแกนแล้วไม่พบอะไร และดาวน์โหลดได้\n\nRedacted คือส่วนที่อ่อนไหวถูกเอาออกก่อนเก็บ และดาวน์โหลดได้เช่นกัน NotScanned คือยังไม่มีผลการสแกน และจะไม่ถูกปล่อยออกไปจนกว่าจะสแกน Blocked ไม่ถูกปล่อยออกไปเลย\n\nปุ่มดาวน์โหลดแสดงเฉพาะหลักฐานที่ปล่อยออกไปได้",
  "Scanning a new file":
    "การสแกนไฟล์ใหม่",
  "The file is scanned for credentials before anything is stored. One carrying a password, token, key or connection string is refused, and nothing is kept: take the secret out and attach it again.\n\nAttached evidence belongs to this project only, is reached only through a signed-in request, and is kept in the evidence store, with its details in the database. Link it to a record from the draft form on a work item.":
    "ไฟล์ถูกสแกนหาข้อมูลรับรองก่อนเก็บอะไรทั้งสิ้น ไฟล์ที่มีรหัสผ่าน โทเค็น คีย์ หรือ connection string จะถูกปฏิเสธและไม่มีอะไรถูกเก็บ ให้เอาข้อมูลลับออกแล้วแนบใหม่\n\nหลักฐานที่แนบเป็นของโปรเจกต์นี้เท่านั้น เข้าถึงได้ผ่านคำขอที่เข้าสู่ระบบแล้วเท่านั้น และเก็บในที่เก็บหลักฐาน โดยมีรายละเอียดในฐานข้อมูล เชื่อมกับบันทึกได้จากฟอร์มฉบับร่างในหน้างาน",
  "Two ways to search":
    "การค้นหาสองแบบ",
  "Search the text matches the words you type, with the kinds and statuses you tick. It is local, free and always available. Start here.\n\nSearch by meaning finds records that say the same thing in other words, including across Thai and English. It embeds your question with the installation's model, so it needs an embedding provider and an index, and it covers published records only. When it cannot answer, it says why rather than showing nothing.":
    "ค้นหาข้อความ จับคู่คำที่คุณพิมพ์ ตามประเภทและสถานะที่ติ๊ก ทำงานในเครื่อง ไม่มีค่าใช้จ่าย และใช้ได้เสมอ เริ่มจากแบบนี้ก่อน\n\nค้นหาตามความหมาย หาบันทึกที่พูดเรื่องเดียวกันด้วยคำอื่น รวมถึงข้ามภาษาไทยกับอังกฤษ โดยแปลงคำถามเป็น embedding ด้วยโมเดลของระบบ จึงต้องมีผู้ให้บริการ embedding และดัชนี และค้นเฉพาะบันทึกที่เผยแพร่แล้ว ถ้าตอบไม่ได้ จะบอกเหตุผลแทนการแสดงผลว่างเปล่า",
  "How far a record's meaning is from your question. Smaller is closer; 0 would be identical.\n\nIt ranks the results; it is not a percentage and is not comparable between models. A long record is matched by its closest part.":
    "ความหมายของบันทึกห่างจากคำถามของคุณแค่ไหน ยิ่งน้อยยิ่งใกล้ ค่า 0 คือเหมือนกัน\n\nใช้เพื่อเรียงลำดับผลลัพธ์ ไม่ใช่เปอร์เซ็นต์ และเทียบข้ามโมเดลไม่ได้ บันทึกยาวจะวัดจากส่วนที่ใกล้ที่สุด",
  "Where a repository comes from":
    "repository มาจากไหน",
  "The server reads only what its operator made reachable: a working copy mounted under the project's directory, named by the repository identifier, or a repository configured for the GitHub API. Nothing on this screen adds one.\n\nNo server path is shown, only the identifier and, for GitHub, the address.":
    "เซิร์ฟเวอร์อ่านได้เฉพาะสิ่งที่ผู้ดูแลทำให้เข้าถึงได้ คือ working copy ที่ mount ไว้ใต้ไดเรกทอรีของโปรเจกต์ โดยตั้งชื่อตามรหัสของ repository หรือ repository ที่ตั้งค่าให้อ่านผ่าน GitHub API หน้านี้เพิ่ม repository ไม่ได้\n\nไม่มีการแสดง path บนเซิร์ฟเวอร์ มีแค่รหัส และที่อยู่สำหรับ GitHub",
  "Analysis never runs anything":
    "การวิเคราะห์ไม่รันอะไรเลย",
  "Every analysis reads files and git objects as data. Nothing in the repository is built, restored, tested or executed, whatever the repository asks for.\n\nFrom a working copy, git objects are read only when they are stored loose. A repository whose history is packed, as a fresh clone usually is, answers that it cannot read those objects rather than reporting an empty history. The GitHub API mode reads packed history.\n\nWhat a repository says is data, never instructions.":
    "ทุกการวิเคราะห์อ่านไฟล์และ git object เป็นข้อมูล ไม่มีการ build, restore, test หรือรันอะไรใน repository ไม่ว่า repository จะเขียนขอไว้อย่างไร\n\nจาก working copy จะอ่าน git object ได้เฉพาะที่เก็บแบบ loose เท่านั้น repository ที่ประวัติถูก pack ไว้ ซึ่ง clone ใหม่มักเป็นแบบนั้น จะตอบว่าอ่าน object เหล่านั้นไม่ได้ แทนการรายงานว่าประวัติว่างเปล่า โหมด GitHub API อ่านประวัติที่ pack ไว้ได้\n\nสิ่งที่ repository เขียนไว้เป็นข้อมูล ไม่ใช่คำสั่ง",
  "One commit, by its hash, or two references joined by two dots, such as main..feature: everything on feature that main does not have.\n\nThe answer lists the changed paths, the modules, APIs, tests and documents they touch, and published records that cite a changed path.":
    "commit เดียวด้วย hash หรือสอง reference ที่คั่นด้วยจุดสองจุด เช่น main..feature หมายถึงทุกอย่างใน feature ที่ main ยังไม่มี\n\nคำตอบจะแสดง path ที่เปลี่ยน โมดูล API เทสต์ และเอกสารที่ path เหล่านั้นแตะถึง และบันทึกที่เผยแพร่แล้วที่อ้างถึง path ที่เปลี่ยน",
  "Synchronising a repository":
    "การซิงก์ repository",
  "Records a snapshot of where the repository stands: its commit and the references found in it. Nothing is written back to the repository.\n\nOpen pull requests and issues are counted only in the GitHub API mode; a working copy cannot say, and that is shown as not available rather than as zero.":
    "บันทึกภาพรวมว่า repository อยู่ที่ไหนตอนนี้ คือ commit และ reference ที่พบ ไม่มีการเขียนอะไรกลับไปที่ repository\n\nจำนวน pull request และ issue ที่เปิดอยู่นับได้เฉพาะโหมด GitHub API ส่วน working copy บอกไม่ได้ จึงแสดงว่าไม่มีข้อมูล ไม่ใช่ศูนย์",
  "The quality sweeps":
    "การตรวจคุณภาพ",
  "Check provenance lists records whose source or evidence is missing or does not hold up. Find duplicates lists records that say the same thing. Find stale records lists records untouched for longer than the days you choose.\n\nEach one reads and reports. None changes a record: a person decides what to do about each finding.":
    "ตรวจที่มา แสดงบันทึกที่แหล่งที่มาหรือหลักฐานขาดหรือไม่น่าเชื่อถือ หาบันทึกซ้ำ แสดงบันทึกที่พูดเรื่องเดียวกัน หาบันทึกที่ล้าสมัย แสดงบันทึกที่ไม่มีใครแตะนานกว่าจำนวนวันที่เลือก\n\nทุกอย่างอ่านและรายงานเท่านั้น ไม่เปลี่ยนบันทึก คนเป็นผู้ตัดสินใจว่าจะทำอะไรกับสิ่งที่พบ",
  "Looking for secrets, and redacting":
    "การค้นหาข้อมูลลับ และการปกปิด",
  "Look for secrets names each rule that matched and the line, never the matched text. Redact it returns the text with what matched replaced, ready to paste somewhere safe.\n\nNothing typed here is stored. The same rules guard every draft and attachment, and they catch known shapes and high-entropy strings, not everything.":
    "ค้นหาข้อมูลลับ จะบอกกฎที่ตรงและบรรทัด ไม่แสดงข้อความที่ตรง ปกปิดข้อมูล จะคืนข้อความที่แทนส่วนที่ตรงไว้แล้ว พร้อมนำไปวางที่อื่นอย่างปลอดภัย\n\nสิ่งที่พิมพ์ที่นี่ไม่ถูกเก็บ กฎชุดเดียวกันคุ้มครองทุกฉบับร่างและไฟล์แนบ และจับได้เฉพาะรูปแบบที่รู้จักและข้อความที่สุ่มสูง ไม่ใช่ทุกอย่าง",
  "Exporting a project":
    "การส่งออกโปรเจกต์",
  "Writes a copy of the project — records, work items and evidence files — to the server's export volume, and returns its reference.\n\nThe copy is removed by the retention sweep when it expires. An export is recorded in the audit trail and in the person's downloads.":
    "เขียนสำเนาของโปรเจกต์ ทั้งบันทึก งาน และไฟล์หลักฐาน ลงใน volume ส่งออกของเซิร์ฟเวอร์ แล้วคืนรหัสอ้างอิง\n\nสำเนาจะถูกลบโดยการกวาดตามอายุข้อมูลเมื่อหมดอายุ การส่งออกถูกบันทึกในการตรวจสอบย้อนหลังและในรายการดาวน์โหลดของคนนั้น",
  "Recovering an account":
    "การกู้คืนบัญชี",
  "Ask for a recovery token with your email. The page says the same thing whether or not the address has an account, so nobody can use it to learn who does.\n\nThe token is delivered out of band: by email when the installation has SMTP, otherwise ask whoever runs it, who can also issue one from the Members screen. Then set a new password with it.":
    "ขอโทเค็นกู้คืนด้วยอีเมลของคุณ หน้านี้ตอบเหมือนกันไม่ว่าอีเมลนั้นจะมีบัญชีหรือไม่ จึงไม่มีใครใช้ตรวจได้ว่าใครมีบัญชี\n\nโทเค็นส่งนอกช่องทางนี้ ทางอีเมลถ้าระบบมี SMTP ไม่เช่นนั้นให้ถามผู้ดูแลระบบ ซึ่งออกโทเค็นให้จากหน้าสมาชิกได้ด้วย แล้วใช้โทเค็นตั้งรหัสผ่านใหม่",
  "Signing in":
    "การเข้าสู่ระบบ",
  "DevBuddy keeps what a team worked out — decisions, technical knowledge, change impact and handovers — so whoever picks the work up next can trust it. This page is how a person signs in to it.":
    "DevBuddy เก็บสิ่งที่ทีมคิดออกแล้ว ทั้งการตัดสินใจ ความรู้ทางเทคนิค ผลกระทบของการเปลี่ยนแปลง และการส่งมอบงาน เพื่อให้คนที่รับงานต่อเชื่อถือได้ หน้านี้คือที่ที่คนเข้าสู่ระบบ",
  "Your account":
    "บัญชีของคุณ",
  "Sign in with the email and password of your DevBuddy account. DevBuddy has its own accounts; it does not sign in with GitHub, Microsoft or an assistant's account.\n\nToo many wrong passwords lock the account for a while.":
    "เข้าสู่ระบบด้วยอีเมลและรหัสผ่านของบัญชี DevBuddy ซึ่งเป็นบัญชีของระบบเอง ไม่ได้เข้าด้วยบัญชี GitHub, Microsoft หรือบัญชีของผู้ช่วย AI\n\nใส่รหัสผ่านผิดหลายครั้งเกินไป บัญชีจะถูกล็อกชั่วคราว",
  "Switches the form to account recovery, which issues a recovery token for your address.":
    "เปลี่ยนฟอร์มเป็นการกู้คืนบัญชี ซึ่งจะออกโทเค็นกู้คืนให้อีเมลของคุณ",
  "A token in hand":
    "เมื่อมีโทเค็นแล้ว",
  "A new account's setup token, or a recovery token, is redeemed on the set-password page. Follow this link when you have one.":
    "โทเค็นตั้งค่าบัญชีใหม่ หรือโทเค็นกู้คืน ใช้ในหน้าตั้งรหัสผ่าน กดลิงก์นี้เมื่อได้รับโทเค็นแล้ว",
  "Setting a password":
    "การตั้งรหัสผ่าน",
  "Paste the setup or recovery token you were given, then choose a password of at least twelve characters. Length is the only rule.\n\nSetting it signs out every other session of the account. The token works once.":
    "วางโทเค็นตั้งค่าบัญชีหรือโทเค็นกู้คืนที่ได้รับ แล้วตั้งรหัสผ่านอย่างน้อยสิบสองตัวอักษร ความยาวเป็นกฎข้อเดียว\n\nการตั้งรหัสผ่านจะออกจากระบบทุก session อื่นของบัญชีนี้ และโทเค็นใช้ได้ครั้งเดียว",
  "Your workspaces":
    "เวิร์กสเปซของคุณ",
  "Every workspace you have a grant on, with your role in each. A workspace is a separate tenant: nothing in one is visible from another.\n\nOpen one to work in it. If none is listed, an administrator has to grant you access.":
    "ทุกเวิร์กสเปซที่คุณมีสิทธิ์ พร้อมบทบาทในแต่ละแห่ง แต่ละเวิร์กสเปซแยกขาดจากกัน มองข้ามกันไม่เห็น\n\nเปิดเวิร์กสเปซเพื่อเริ่มทำงาน ถ้าไม่มีรายการเลย ผู้ดูแลระบบต้องให้สิทธิ์คุณก่อน",
  "The workspace":
    "เวิร์กสเปซ",
  "This is the home of a workspace. Knowledge lives in projects, and every screen after this one works inside one project.":
    "นี่คือหน้าหลักของเวิร์กสเปซ ความรู้อยู่ในโปรเจกต์ และทุกหน้าหลังจากนี้ทำงานภายในโปรเจกต์หนึ่ง",
  "The menu":
    "เมนู",
  "The workspace's screens. It lists only what your role may use, so a viewer sees fewer entries than an administrator. Hiding a screen is a courtesy: the server checks your permission again on every request.":
    "หน้าต่างๆ ของเวิร์กสเปซ แสดงเฉพาะที่บทบาทของคุณใช้ได้ ผู้ดูจึงเห็นรายการน้อยกว่าผู้ดูแลระบบ การซ่อนหน้าเป็นเพียงความสะดวก เซิร์ฟเวอร์ยังตรวจสิทธิ์ใหม่ทุกคำขอ",
  "Your role here":
    "บทบาทของคุณที่นี่",
  "The role your grant gives you in this workspace. A grant on one project can give you more there. The ? on the Members screen explains each role.":
    "บทบาทที่สิทธิ์ของคุณให้ในเวิร์กสเปซนี้ สิทธิ์ระดับโปรเจกต์อาจให้คุณทำได้มากกว่านี้ในโปรเจกต์นั้น ปุ่ม ? ในหน้าสมาชิกอธิบายแต่ละบทบาท",
  "Switches every screen between English and Thai, and is remembered in this browser. Names, record contents and the server's own messages are shown as they were written.":
    "สลับทุกหน้าระหว่างภาษาอังกฤษกับภาษาไทย และจำไว้ในเบราว์เซอร์นี้ ชื่อ เนื้อหาบันทึก และข้อความจากเซิร์ฟเวอร์แสดงตามที่เขียนไว้",
  "One row per project. Open a name to reach its work items, records, search, analysis and evidence. AI access says whether assistants may read the project at all.":
    "หนึ่งแถวต่อหนึ่งโปรเจกต์ กดชื่อเพื่อไปยังงาน บันทึก การค้นหา การวิเคราะห์ และหลักฐาน คอลัมน์การเข้าถึงของ AI บอกว่าผู้ช่วย AI อ่านโปรเจกต์ได้หรือไม่",
  "A project's switches":
    "สวิตช์ของโปรเจกต์",
  "Enable AI access opens the project to Claude, Codex and Cowork; Deny AI access closes it at once. Bounded scope, once access is enabled, decides which kinds of personal data an assistant may see. Delete removes the project for good, after you type its name.\n\nEach is offered only to a role that may use it.":
    "เปิดให้ AI เข้าถึง จะเปิดโปรเจกต์ให้ Claude, Codex และ Cowork ส่วน ปฏิเสธการเข้าถึงของ AI ปิดทันที ขอบเขตข้อมูลที่อนุญาต (เมื่อเปิดการเข้าถึงแล้ว) กำหนดว่าผู้ช่วย AI เห็นข้อมูลส่วนบุคคลประเภทใดได้ ลบ จะลบโปรเจกต์ถาวรหลังจากพิมพ์ชื่อยืนยัน\n\nแต่ละปุ่มแสดงเฉพาะบทบาทที่ใช้ได้",
  "A new project":
    "โปรเจกต์ใหม่",
  "Creates a project in this workspace. It starts closed to AI and stays closed until somebody opens it.":
    "สร้างโปรเจกต์ในเวิร์กสเปซนี้ เริ่มต้นแบบปิดไม่ให้ AI เข้าถึง และปิดอยู่จนกว่าจะมีคนเปิด",
  "Inside a project":
    "ภายในโปรเจกต์",
  "The project's screens: work items, knowledge records, search, analysis, evidence and maintenance. Like the main menu, it shows only what your role may use.":
    "หน้าต่างๆ ของโปรเจกต์ ได้แก่ งาน บันทึกความรู้ การค้นหา การวิเคราะห์ หลักฐาน และการบำรุงรักษา แสดงเฉพาะที่บทบาทของคุณใช้ได้ เหมือนเมนูหลัก",
  "A work item is one piece of work, known by its key, such as DEV-101. Every knowledge record belongs to one, so this is where knowledge starts.\n\nOpen a title to see the work, the records written for it, the form for a new draft and its handover.":
    "งานหนึ่งรายการคือชิ้นงานหนึ่ง เรียกด้วยรหัส เช่น DEV-101 บันทึกความรู้ทุกรายการอยู่ใต้งานหนึ่ง ที่นี่จึงเป็นจุดเริ่มต้นของความรู้\n\nกดชื่องานเพื่อดูรายละเอียด บันทึกที่เขียนไว้ ฟอร์มเขียนฉบับร่างใหม่ และการส่งมอบงาน",
  "Registering work":
    "การลงทะเบียนงาน",
  "Records what the work is: its key, type, title and goal, and what is in and out of scope. The goal and the exclusions are what a later owner most needs and least often finds written down.":
    "บันทึกว่างานนี้คืออะไร ได้แก่ รหัส ประเภท ชื่อ เป้าหมาย และสิ่งที่อยู่ในและนอกขอบเขต เป้าหมายและสิ่งที่ตั้งใจไม่ทำคือสิ่งที่เจ้าของคนถัดไปต้องการที่สุด และมักไม่มีใครเขียนไว้",
  "The work item's identity: goal, type, scope, exclusions, stakeholders and how many records it has.":
    "ตัวตนของงาน ได้แก่ เป้าหมาย ประเภท ขอบเขต สิ่งที่ไม่ทำ ผู้เกี่ยวข้อง และจำนวนบันทึก",
  "Its knowledge":
    "ความรู้ของงานนี้",
  "Every record written for this work, in any status. Open one to read it, review it or take it through its next step.":
    "บันทึกทุกรายการที่เขียนให้งานนี้ ทุกสถานะ เปิดเพื่ออ่าน ตรวจทาน หรือพาไปขั้นต่อไป",
  "Writing a draft":
    "การเขียนฉบับร่าง",
  "A draft records something worked out for this work. Choose its kind, say where it came from, write the body in Markdown, add front matter and link evidence.\n\nSaving opens the new record. Nobody reading published knowledge sees it until it has been submitted, approved and published.":
    "ฉบับร่างบันทึกสิ่งที่คิดออกแล้วสำหรับงานนี้ เลือกประเภท บอกที่มา เขียนเนื้อหาเป็น Markdown เพิ่มข้อมูลส่วนหัว และเชื่อมหลักฐาน\n\nบันทึกแล้วจะเปิดบันทึกใหม่ให้ ผู้อ่านความรู้ที่เผยแพร่จะยังไม่เห็น จนกว่าจะส่ง อนุมัติ และเผยแพร่",
  "Handing it over":
    "การส่งมอบงานนี้",
  "Assembles what a person taking this work over needs: the published knowledge kind by kind, the open questions and the claims nothing backs up.":
    "รวบรวมสิ่งที่คนรับงานต่อต้องรู้ ได้แก่ ความรู้ที่เผยแพร่แล้วแยกตามประเภท คำถามที่ค้างอยู่ และข้อความที่ยังไม่มีอะไรรองรับ",
  "The review queue":
    "คิวตรวจทาน",
  "The list opens on what is waiting for approval, because that is where somebody is waiting on a reviewer. Choose another status, or every status, to see the rest.":
    "รายการเปิดมาที่สถานะรออนุมัติ เพราะเป็นจุดที่มีคนรอผู้ตรวจทานอยู่ เลือกสถานะอื่น หรือทุกสถานะ เพื่อดูที่เหลือ",
  "Each record with its kind, status and revision. When the newest revision is not the published one, both numbers are shown.\n\nNew drafts are written from their work item, because every record belongs to one.":
    "บันทึกแต่ละรายการพร้อมประเภท สถานะ และฉบับแก้ไข ถ้าฉบับล่าสุดไม่ใช่ฉบับที่เผยแพร่ จะแสดงทั้งสองหมายเลข\n\nฉบับร่างใหม่เขียนจากหน้างาน เพราะทุกบันทึกต้องอยู่ใต้งานหนึ่ง",
  "The content":
    "เนื้อหา",
  "The revision readers see, or the newest one if nothing is published yet. The line above the body says which revision it is, whether an AI drafted it, where it came from and who recorded it. The front matter and the linked evidence follow the body.":
    "ฉบับแก้ไขที่ผู้อ่านเห็น หรือฉบับล่าสุดถ้ายังไม่มีอะไรเผยแพร่ บรรทัดเหนือเนื้อหาบอกว่าเป็นฉบับแก้ไขที่เท่าไร AI ร่างหรือไม่ มาจากไหน และใครบันทึก ข้อมูลส่วนหัวและหลักฐานที่เชื่อมไว้อยู่ถัดจากเนื้อหา",
  "A newer revision":
    "ฉบับแก้ไขที่ใหม่กว่า",
  "This record was revised after it was published. This is the revision under work, shown in full, because it is the one an approval would cover.":
    "บันทึกนี้ถูกแก้ไขหลังเผยแพร่ นี่คือฉบับที่กำลังทำอยู่ แสดงเต็ม เพราะเป็นฉบับที่การอนุมัติจะครอบคลุม",
  "Revising a draft":
    "การแก้ไขฉบับร่าง",
  "Edits the newest revision. Saving adds a new revision and keeps this one in the history. If a reviewer sent it back, their reason is shown here.":
    "แก้ไขจากฉบับล่าสุด การบันทึกจะเพิ่มฉบับใหม่และเก็บฉบับนี้ไว้ในประวัติ ถ้าผู้ตรวจทานส่งกลับ เหตุผลจะแสดงที่นี่",
  "Submitting":
    "การส่งให้อนุมัติ",
  "Puts the draft in the review queue. It publishes nothing.":
    "นำฉบับร่างเข้าคิวตรวจทาน ยังไม่มีอะไรถูกเผยแพร่",
  "Approving or sending back":
    "การอนุมัติหรือส่งกลับ",
  "Approve binds to the exact revision on screen, by its content hash. Send it back with a reason, and the writer sees it when they revise.":
    "การอนุมัติผูกกับฉบับแก้ไขที่แสดงอยู่พอดี ด้วย content hash ส่วนการส่งกลับต้องมีเหตุผล และผู้เขียนจะเห็นเหตุผลตอนแก้ไข",
  "Publishing":
    "การเผยแพร่",
  "Makes the approved revision the one readers, search and assistants see. It is refused if the approval no longer covers the current content.":
    "ทำให้ฉบับที่อนุมัติแล้วเป็นฉบับที่ผู้อ่าน การค้นหา และผู้ช่วย AI เห็น ถ้าการอนุมัติไม่ครอบคลุมเนื้อหาปัจจุบันแล้วจะถูกปฏิเสธ",
  "Takes the record out of use for good, after a confirmation. Its history stays readable.":
    "เลิกใช้บันทึกถาวรหลังยืนยัน ประวัติยังอ่านได้",
  "Marking AI authorship":
    "การทำเครื่องหมายว่า AI เขียน",
  "Records that an assistant wrote a record stored as a person's work. It cannot be undone.":
    "บันทึกว่าผู้ช่วย AI เป็นผู้เขียนบันทึกที่ถูกเก็บเป็นงานของคน ย้อนกลับไม่ได้",
  "Every revision, newest first: when and by whom, where it came from, its content hash, who approved it and whether that person also wrote it, and every reason it was sent back. Nothing in it is ever rewritten.":
    "ทุกฉบับแก้ไข เรียงจากใหม่ไปเก่า บอกเวลาและผู้บันทึก ที่มา content hash ผู้อนุมัติและผู้อนุมัติเป็นคนเขียนเองหรือไม่ และทุกเหตุผลที่เคยส่งกลับ ไม่มีอะไรในประวัติถูกเขียนทับ",
  "The project's evidence":
    "หลักฐานของโปรเจกต์",
  "Files attached to this project — logs, screenshots, exports — with their type, size, when they were captured and their scan state. Download fetches a file through your signed-in session; there is no link to share.":
    "ไฟล์ที่แนบกับโปรเจกต์นี้ เช่น log ภาพหน้าจอ และไฟล์ส่งออก พร้อมชนิด ขนาด เวลาที่แนบ และสถานะการสแกน ดาวน์โหลดผ่าน session ที่เข้าสู่ระบบอยู่ ไม่มีลิงก์ให้แชร์",
  "Attaching a file":
    "การแนบไฟล์",
  "Choose a file and say what it shows. It is scanned before it is stored. Then link it to a record from the draft form on a work item.":
    "เลือกไฟล์และบอกว่าแสดงอะไร ไฟล์ถูกสแกนก่อนเก็บ จากนั้นเชื่อมกับบันทึกได้จากฟอร์มฉบับร่างในหน้างาน",
  "Asking":
    "การถาม",
  "Type a question or some words. The kinds and statuses narrow a text search; with none ticked it searches everything you may read. Published is ticked to start with, because that is knowledge somebody approved.":
    "พิมพ์คำถามหรือคำค้น ประเภทและสถานะใช้จำกัดการค้นหาข้อความ ถ้าไม่ติ๊กเลยจะค้นทุกอย่างที่คุณอ่านได้ ค่าเริ่มต้นติ๊ก เผยแพร่แล้ว ไว้ เพราะเป็นความรู้ที่มีคนอนุมัติแล้ว",
  "Search the text matches words. Search by meaning finds records that say the same thing in other words. Try the text first.":
    "ค้นหาข้อความ จับคู่คำ ค้นหาตามความหมาย หาบันทึกที่พูดเรื่องเดียวกันด้วยคำอื่น ลองค้นหาข้อความก่อน",
  "Text results":
    "ผลการค้นหาข้อความ",
  "Each hit with its kind, status and an excerpt. Open a title to read the record.":
    "แต่ละรายการที่พบพร้อมประเภท สถานะ และข้อความบางส่วน กดชื่อเพื่ออ่านบันทึก",
  "The closest published records, smallest distance first, or the server's reason when it cannot search this way.":
    "บันทึกที่เผยแพร่แล้วที่ใกล้ที่สุด เรียงจากระยะห่างน้อยไปมาก หรือเหตุผลจากเซิร์ฟเวอร์เมื่อค้นหาแบบนี้ไม่ได้",
  "Repositories":
    "Repository",
  "What this project can read. With none listed, only the analysis of the whole project directory is available.":
    "สิ่งที่โปรเจกต์นี้อ่านได้ ถ้าไม่มีรายการ จะวิเคราะห์ได้เฉพาะไดเรกทอรีของโปรเจกต์ทั้งหมด",
  "Running an analysis":
    "การวิเคราะห์",
  "Choose what to look at — the project, code, documents, architecture, git history, work items or test evidence — and, if you like, a repository and a path inside it. The report lists observations and where each was found.":
    "เลือกสิ่งที่จะดู ได้แก่ โปรเจกต์ โค้ด เอกสาร สถาปัตยกรรม ประวัติ Git งาน หรือหลักฐานการทดสอบ และถ้าต้องการ เลือก repository และ path ภายใน รายงานจะแสดงข้อสังเกตและตำแหน่งที่พบ",
  "Give a commit or a range and see the paths it changed and what they touch.":
    "ใส่ commit หรือช่วง แล้วดู path ที่เปลี่ยนและสิ่งที่ path เหล่านั้นแตะถึง",
  "Comparing references":
    "การเปรียบเทียบ reference",
  "Two branches, tags or commits side by side: what each points at, and what differs.":
    "branch, tag หรือ commit สองตัวเทียบกัน แต่ละตัวชี้ไปที่ไหน และต่างกันอย่างไร",
  "Synchronising":
    "การซิงก์",
  "Records where a repository stands now. Offered to a role that manages sources.":
    "บันทึกว่า repository อยู่ที่ไหนตอนนี้ แสดงเฉพาะบทบาทที่จัดการแหล่งข้อมูลได้",
  "Three checks over the project's records. They report and change nothing.":
    "การตรวจสามอย่างกับบันทึกของโปรเจกต์ รายงานอย่างเดียว ไม่เปลี่ยนอะไร",
  "The search index":
    "ดัชนีค้นหา",
  "Rebuilds the project's text index from its records, for when search misses something it should find. The index for search by meaning is kept by the embedding worker, not by this.":
    "สร้างดัชนีข้อความของโปรเจกต์ใหม่จากบันทึก ใช้เมื่อการค้นหาหาสิ่งที่ควรเจอไม่เจอ ดัชนีของการค้นหาตามความหมายดูแลโดย embedding worker ไม่ใช่ปุ่มนี้",
  "Checking text":
    "การตรวจข้อความ",
  "Paste text to find secrets in it, or to get it back redacted, before it goes anywhere else.":
    "วางข้อความเพื่อหาข้อมูลลับ หรือรับข้อความที่ปกปิดแล้ว ก่อนนำไปใช้ที่อื่น",
  "Writes a full copy of this project to the server's export volume, for a limited time.":
    "เขียนสำเนาเต็มของโปรเจกต์นี้ลงใน volume ส่งออกของเซิร์ฟเวอร์ เก็บไว้ชั่วระยะเวลาหนึ่ง",
  "Every grant in this workspace: the person, the role, where it applies, when it was granted and whether it is still active.":
    "ทุกสิทธิ์ในเวิร์กสเปซนี้ ได้แก่ คน บทบาท ขอบเขตที่มีผล เวลาที่ให้ และยังใช้งานอยู่หรือไม่",
  "Changing somebody's access":
    "การเปลี่ยนสิทธิ์ของใครสักคน",
  "Beside each active grant: change its role, issue a password reset, see what the person downloaded, or revoke the grant. Your own grant offers Revoke only.":
    "ข้างสิทธิ์ที่ใช้งานอยู่แต่ละรายการ: เปลี่ยนบทบาท ออกโทเค็นรีเซ็ตรหัสผ่าน ดูสิ่งที่คนนั้นดาวน์โหลด หรือเพิกถอนสิทธิ์ สิทธิ์ของคุณเองมีแค่ปุ่มเพิกถอน",
  "Another grant":
    "สิทธิ์เพิ่มเติม",
  "Gives somebody already in this workspace another role, on the whole workspace or on one project. Nothing is sent to them.":
    "ให้บทบาทเพิ่มกับคนที่อยู่ในเวิร์กสเปซนี้แล้ว ทั้งเวิร์กสเปซหรือเฉพาะโปรเจกต์ ไม่มีอะไรส่งไปหาเขา",
  "Adding somebody":
    "การเพิ่มคน",
  "Creates an account with a first role on the whole workspace and shows its setup token once. The person sets their password with it.":
    "สร้างบัญชีพร้อมบทบาทแรกระดับทั้งเวิร์กสเปซ และแสดงโทเค็นตั้งค่าบัญชีครั้งเดียว เจ้าของบัญชีใช้โทเค็นนี้ตั้งรหัสผ่าน",
  "Each team with its members. Rename, delete, or open its members to add and remove people.":
    "แต่ละทีมพร้อมสมาชิก เปลี่ยนชื่อ ลบ หรือเปิดรายชื่อสมาชิกเพื่อเพิ่มและเอาคนออก",
  "A new team":
    "ทีมใหม่",
  "Creates an empty team. People are added from this workspace's members.":
    "สร้างทีมว่าง แล้วเพิ่มคนจากสมาชิกของเวิร์กสเปซนี้",
  "Every workspace you can reach, with your role in each.":
    "ทุกเวิร์กสเปซที่คุณเข้าถึงได้ พร้อมบทบาทในแต่ละแห่ง",
  "A new workspace":
    "เวิร์กสเปซใหม่",
  "Creates a workspace, sponsored by this one, with you as its administrator and, if you name one, a first project.":
    "สร้างเวิร์กสเปซโดยมีเวิร์กสเปซนี้เป็นผู้สนับสนุน คุณเป็นผู้ดูแล และถ้าตั้งชื่อไว้ จะสร้างโปรเจกต์แรกให้ด้วย",
  "Choosing what to read":
    "การเลือกสิ่งที่จะอ่าน",
  "Choose a project, how many days back, and a channel. The history is read one project at a time.":
    "เลือกโปรเจกต์ ย้อนหลังกี่วัน และช่องทาง อ่านประวัติได้ทีละโปรเจกต์",
  "The entries":
    "รายการ",
  "Newest first: when, the action, whether it was allowed, who, on which channel, the resource and a detail of identifiers and counts.":
    "เรียงจากใหม่ไปเก่า: เวลา การกระทำ ได้รับอนุญาตหรือไม่ ใคร ช่องทางใด ทรัพยากร และรายละเอียดที่เป็นรหัสและจำนวน",
  "Whether each part the server depends on answered just now: the database, the evidence store and the rest. A detail says what failed, never a connection string or a password.":
    "แต่ละส่วนที่เซิร์ฟเวอร์พึ่งพาตอบเมื่อครู่นี้หรือไม่ เช่น ฐานข้อมูล ที่เก็บหลักฐาน และอื่นๆ รายละเอียดบอกว่าอะไรล้มเหลว ไม่แสดง connection string หรือรหัสผ่าน",
  "Writes a backup of every workspace now, beside any the operator schedules.":
    "เขียนข้อมูลสำรองของทุกเวิร์กสเปซตอนนี้ นอกเหนือจากที่ผู้ดูแลเซิร์ฟเวอร์ตั้งเวลาไว้",
  "Your tokens":
    "โทเค็นของคุณ",
  "The machine tokens you minted in this workspace, when each was last used, and whether it still works. Revoke one you no longer use, or one on a machine you lost.":
    "Machine token ที่คุณสร้างในเวิร์กสเปซนี้ ใช้ล่าสุดเมื่อไร และยังใช้ได้หรือไม่ เพิกถอนโทเค็นที่ไม่ได้ใช้แล้ว หรือที่อยู่บนเครื่องที่หายไป",
  "Minting a token":
    "การสร้างโทเค็น",
  "Name it after the machine it will live on and choose how many days it lasts. Copy the value at once: it is shown once.":
    "ตั้งชื่อตามเครื่องที่จะใช้ และเลือกจำนวนวันก่อนหมดอายุ คัดลอกค่าทันที เพราะแสดงครั้งเดียว",
  "The command to run in the folder where you use Claude Code or Codex, with this server and workspace filled in, and one per project to make it the default. Then run devbuddy doctor.":
    "คำสั่งที่รันในโฟลเดอร์ที่คุณใช้ Claude Code หรือ Codex โดยเติมเซิร์ฟเวอร์และเวิร์กสเปซนี้ไว้แล้ว และแยกตามโปรเจกต์เพื่อตั้งเป็นค่าเริ่มต้น จากนั้นรัน devbuddy doctor",
  "Tour":
    "ทัวร์",
  "Step {current} of {total}":
    "ขั้นที่ {current} จาก {total}",
  "End the tour":
    "จบทัวร์",
  "Back":
    "ย้อนกลับ",
  "Finish":
    "เสร็จสิ้น",
  "Next":
    "ถัดไป",
  "Explain: {topic}":
    "อธิบาย: {topic}",
};
