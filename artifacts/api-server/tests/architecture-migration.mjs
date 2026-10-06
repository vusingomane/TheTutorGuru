import assert from "node:assert/strict";
import { createRequire } from "node:module";
import { randomUUID, createHash } from "node:crypto";
import { writeFile, mkdir, readFile, unlink } from "node:fs/promises";
const require = createRequire(new URL("../../../lib/db/package.json", import.meta.url));
const { Pool } = require("pg");
const pool = new Pool({ connectionString: process.env.DATABASE_URL });
const origin = process.env.TEST_ORIGIN || `https://${process.env.REPLIT_DEV_DOMAIN}`;
if (!process.env.REPLIT_DEV_DOMAIN && !process.env.TEST_ORIGIN) throw new Error("Set TEST_ORIGIN to the development preview URL.");
const prefix = `migration-${Date.now()}-${randomUUID().slice(0, 6)}`;
const password = `Test-${randomUUID()}aA9!`;
const users = [], fixtures = { profiles: [], courses: [], sessions: [], learners: [], enrollments: [], paths: [] };
let checks = 0;
let completed = false;
function ok(condition, label) { assert(condition, label); checks++; }
class Client {
  cookies = new Map(); csrf = null;
  async call(path, method = "GET", body, expected = 200, secure = true) {
    if (method !== "GET" && secure && !this.csrf) {
      this.csrf = (await this.call("/auth/csrf")).token;
    }
    const response = await fetch(origin + "/api" + path, {
      method, headers: { "Content-Type": "application/json", Cookie: [...this.cookies].map(([k,v])=>`${k}=${v}`).join("; "), ...(method !== "GET" && secure ? { "X-CSRF-TOKEN": this.csrf } : {}) },
      body: body === undefined ? undefined : JSON.stringify(body)
    });
    for (const cookie of response.headers.getSetCookie()) {
      const [name, ...rest] = cookie.split(";")[0].split("="); this.cookies.set(name, rest.join("="));
    }
    const text = await response.text();
    let data; try { data = text ? JSON.parse(text) : null; } catch { throw new Error(`Non-JSON response at ${path}: ${response.status}`); }
    assert.equal(response.status, expected, `${method} ${path}: ${JSON.stringify(data)}`);
    checks++;
    if (response.ok && ["/auth/login", "/auth/logout"].includes(path)) this.csrf = null;
    return data;
  }
}
async function register(role, grade = null) {
  const email = `${prefix}-${role}-${users.length}@example.com`;
  const client = new Client();
  await client.call("/auth/register", "POST", { email, password }, 201);
  users.push(email);
  const { rows: [mail] } = await pool.query("SELECT link FROM guru_identity_dev_mail WHERE recipient=$1 ORDER BY created_at DESC LIMIT 1", [email]);
  ok(mail, "Private development verification mail was queued");
  const url = new URL(mail.link);
  const userId = url.searchParams.get("userId");
  await client.call("/auth/login", "POST", { email, password }, 401);
  await client.call("/auth/confirm-email", "POST", { userId, token: url.searchParams.get("token") });
  await client.call("/auth/confirm-email", "POST", { userId, token: url.searchParams.get("token") }, 400);
  await client.call("/auth/login", "POST", { email, password });
  const user = { email, userId, role, client, password };
  if (role) {
    user.profile = await client.call("/profile", "PUT", { fullName: `${prefix} ${role}`, role, grade, phone: null });
    fixtures.profiles.push(user.profile.id);
    ok(user.profile.role === role, "Onboarding stores requested role");
  }
  return user;
}
async function cleanup() {
  for (const path of fixtures.paths) {
    const owner = await pool.query("SELECT owner_id FROM guru_uploads WHERE object_path=$1", [path]);
    if (!owner.rowCount || !fixtures.profiles.includes(owner.rows[0].owner_id)) continue;
    if (!/^\/objects\/uploads\/[a-f0-9-]{36}$/.test(path)) throw new Error("Invalid test object path.");
    const parts = process.env.PRIVATE_OBJECT_DIR.replace(/^\/|\/$/g, "").split("/");
    const signed = await fetch("http://127.0.0.1:1106/object-storage/signed-object-url", {
      method: "POST", headers: {"Content-Type":"application/json"},
      body: JSON.stringify({bucket_name:parts.shift(),object_name:parts.join("/") + path.slice("/objects".length),method:"DELETE",expires_at:new Date(Date.now()+900000).toISOString()})
    });
    if (!signed.ok) throw new Error("Unable to sign test-object cleanup.");
    const removed = await fetch((await signed.json()).signed_url, {method:"DELETE"});
    if (!removed.ok && removed.status !== 404) throw new Error("Unable to remove test object.");
  }
  // Only this run's UUIDs/emails, never a broad delete or a legacy ID.
  if (fixtures.sessions.length) await pool.query("DELETE FROM guru_recordings WHERE session_id=ANY($1::uuid[])", [fixtures.sessions]);
  if (fixtures.courses.length) await pool.query("DELETE FROM guru_materials WHERE course_id=ANY($1::uuid[])", [fixtures.courses]);
  if (fixtures.sessions.length) {
    await pool.query("DELETE FROM guru_attendance WHERE session_id=ANY($1::uuid[])", [fixtures.sessions]);
    await pool.query("DELETE FROM guru_sessions WHERE id=ANY($1::uuid[])", [fixtures.sessions]);
  }
  if (fixtures.courses.length) await pool.query("DELETE FROM guru_courses WHERE id=ANY($1::uuid[])", [fixtures.courses]);
  if (fixtures.learners.length) {
    await pool.query("DELETE FROM guru_enrollments WHERE learner_id=ANY($1::uuid[])", [fixtures.learners]);
    await pool.query("DELETE FROM guru_learners WHERE id=ANY($1::uuid[])", [fixtures.learners]);
  }
  if (fixtures.paths.length) await pool.query("DELETE FROM guru_uploads WHERE object_path=ANY($1::text[])", [fixtures.paths]);
  if (fixtures.profiles.length) {
    await pool.query("DELETE FROM guru_identity_invitations WHERE profile_id=ANY($1::text[])", [fixtures.profiles]);
    await pool.query("DELETE FROM guru_identity_profile_links WHERE profile_id=ANY($1::text[])", [fixtures.profiles]);
    await pool.query("DELETE FROM guru_profiles WHERE id=ANY($1::text[])", [fixtures.profiles]);
  }
  await pool.query("DELETE FROM guru_identity_dev_mail WHERE recipient=ANY($1::text[])", [users]);
  await pool.query("DELETE FROM guru_identity_users WHERE normalized_email=ANY($1::text[])", [users.map(e=>e.toUpperCase())]);
}
if (process.argv.includes("--cleanup-retained")) {
  const retained = JSON.parse(await readFile(".data/browser-fixtures.json","utf8"));
  if (!retained.prefix.startsWith("migration-") || !retained.users.every(e=>e.startsWith(retained.prefix))) throw new Error("Invalid test fixture manifest.");
  users.push(...retained.users);
  fixtures.profiles.push(...retained.fixtures.profiles);
  fixtures.courses.push(...(await pool.query("SELECT id FROM guru_courses WHERE tutor_id=ANY($1::text[])",[fixtures.profiles])).rows.map(r=>r.id));
  fixtures.sessions.push(...(await pool.query("SELECT id FROM guru_sessions WHERE course_id=ANY($1::uuid[])",[fixtures.courses])).rows.map(r=>r.id));
  fixtures.learners.push(...(await pool.query("SELECT id FROM guru_learners WHERE parent_id=ANY($1::text[])",[fixtures.profiles])).rows.map(r=>r.id));
  fixtures.paths.push(...(await pool.query("SELECT object_path FROM guru_uploads WHERE owner_id=ANY($1::text[])",[fixtures.profiles])).rows.map(r=>r.object_path));
  await cleanup();
  await pool.end();
  await unlink(".data/browser-fixtures.json");
  console.log("Only retained generated test accounts, their owned test rows, and their private test objects were removed.");
  process.exit(0);
}
try {
  const guest = new Client();
  await guest.call("/healthz");
  await guest.call("/profile", "GET", undefined, 401);
  await guest.call("/learners", "POST", { name: "Bad", email: "x@example.com", grade: 10 }, 400, false);
  const subjects = await guest.call("/subjects");
  ok(subjects.find(s=>s.id==="mathematics").grades.join() === "8,9,10,11,12", "Mathematics grades preserved");
  ok(subjects.find(s=>s.id==="afrikaans").grades.join() === "11,12", "Afrikaans grades preserved");
  const parent = await register("parent");
  const tutor = await register("tutor");
  const learner = await register("learner", 10);
  await parent.client.call("/profile", "PUT", { fullName: "Changed", role: "tutor", grade: null }, 409);
  await learner.client.call("/learners", "POST", { name: "Illegal", email: "x@example.com", grade: 10 }, 403);
  const child = await parent.client.call("/learners", "POST", { name: `${prefix} Child`, email: learner.email, grade: 10 }, 201);
  fixtures.learners.push(child.id);
  await parent.client.call("/enrollments", "POST", { learnerId: child.id, subjectIds: ["afrikaans"] }, 400);
  await parent.client.call("/enrollments", "POST", { learnerId: child.id, subjectIds: ["mathematics","mathematics"] }, 400);
  const enrollment = await parent.client.call("/enrollments", "POST", { learnerId: child.id, subjectIds: ["mathematics"] }, 201);
  fixtures.enrollments.push(enrollment.id);
  ok(enrollment.monthlyAmountZar === 290 && enrollment.paymentStatus === "awaiting_provider" && enrollment.status === "pending", "Pending request priced R290 and cannot unlock content");
  await parent.client.call("/enrollments", "POST", { learnerId: child.id, subjectIds: ["mathematics"] }, 409);
  const changed = await parent.client.call(`/enrollments/${enrollment.id}`, "PATCH", { action: "change", subjectIds: ["mathematics","physical-science"] });
  ok(changed.monthlyAmountZar === 495, "Two subjects cost R495");
  const course = await tutor.client.call("/courses", "POST", { title: `${prefix} Maths`, subjectId: "mathematics", gradeMin: 10, gradeMax: 10, scheduleSummary: "Two weekly sessions" }, 201);
  fixtures.courses.push(course.id);
  const session = await tutor.client.call("/sessions", "POST", { courseId: course.id, title: `${prefix} Algebra`, startsAt: new Date().toISOString(), durationMinutes: 60, meetingUrl: "https://zoom.us/j/123456789", lessonPlan: "Tutor private plan" }, 201);
  fixtures.sessions.push(session.id);
  await tutor.client.call("/sessions", "POST", { courseId: course.id, title: "Bad class", startsAt: new Date().toISOString(), durationMinutes: 30 }, 400);
  await tutor.client.call(`/sessions/${session.id}`, "PATCH", { meetingUrl: "https://zoom.us.evil.test/x" }, 400);
  await parent.client.call(`/sessions/${session.id}`, "PATCH", { status: "completed" }, 403);
  ok((await learner.client.call("/sessions")).length === 0, "Pending learner sees no sessions");
  ok((await parent.client.call("/sessions")).length === 0, "Pending parent sees no sessions");
  await tutor.client.call(`/attendance/${session.id}/${child.id}`, "PUT", { attended: true }, 403);
  await pool.query("UPDATE guru_enrollments SET status='active',payment_status='paid',period_ends_at=now()+interval '30 days' WHERE id=$1", [enrollment.id]);
  const list = await learner.client.call("/sessions");
  ok(list.some(s=>s.id===session.id && s.lessonPlan===null), "Paid learner sees class without tutor private plan");
  ok((await parent.client.call("/sessions")).some(s=>s.id===session.id), "Paid parent sees family class");
  await tutor.client.call(`/sessions/${session.id}`, "PATCH", { status: "completed" });
  await tutor.client.call(`/attendance/${session.id}/${child.id}`, "PUT", { attended: true });
  ok((await learner.client.call("/progress")).some(p=>p.classesAttended===1 && p.attendancePercent===100), "Learner attendance progress calculated");
  ok((await tutor.client.call("/learners")).some(l=>l.id===child.id), "Tutor sees eligible paid learner roster");
  await parent.client.call(`/enrollments/${enrollment.id}`, "PATCH", { action: "cancel" }, 409);
  const overview = await parent.client.call("/overview");
  ok(overview.totalLearners===1 && overview.activeSubjects>=1 && overview.recentActivity.length>0, "Overview real counts and activities");
  // Files: a genuine private upload and registration, not mocked metadata.
  const bytes = new TextEncoder().encode("TheTutorGuru architecture migration test\n");
  const upload = await tutor.client.call("/storage/uploads/request-url", "POST", { name: "architecture-test.txt", contentType: "text/plain", size: bytes.length });
  fixtures.paths.push(upload.objectPath);
  const put = await fetch(upload.uploadURL, { method: "PUT", headers: {"Content-Type":"text/plain"}, body: bytes });
  ok(put.ok, "Genuine direct-to-storage upload completed");
  const material = await tutor.client.call("/materials", "POST", { courseId: course.id, title: "Test notes", objectPath: upload.objectPath, originalFileName: "architecture-test.txt", contentType: "text/plain", sizeBytes: bytes.length }, 201);
  ok((await learner.client.call("/materials")).some(m=>m.id===material.id), "Paid learner sees uploaded material");
  const file = await fetch(origin + "/api/storage" + upload.objectPath, { headers: { Cookie: [...learner.client.cookies].map(([k,v])=>`${k}=${v}`).join("; "), Range: "bytes=0-9" } });
  ok(file.status===206 && (await file.arrayBuffer()).byteLength===10, "Protected file byte ranges work");
  await parent.client.call(`/materials/${material.id}`, "DELETE", undefined, 403);
  await tutor.client.call(`/materials/${material.id}`, "DELETE", undefined, 204);
  await pool.query("UPDATE guru_enrollments SET period_ends_at=now()-interval '1 day' WHERE id=$1", [enrollment.id]);
  ok((await learner.client.call("/sessions")).length===0 && (await learner.client.call("/materials")).length===0, "Expired enrolment removes lesson and file access");
  // Migration: synthetic legacy profile only, does not alter any original row.
  const migrate = await register(null);
  const legacyId = `legacy-test-${randomUUID()}`;
  fixtures.profiles.push(legacyId);
  await pool.query("INSERT INTO guru_profiles(id,full_name,email,role) VALUES($1,$2,$3,'parent')", [legacyId, `${prefix} Legacy Parent`, migrate.email]);
  const legacyChild = await pool.query("INSERT INTO guru_learners(parent_id,name,email,grade) VALUES($1,'Existing child',$2,10) RETURNING id", [legacyId, `${prefix}-legacy-child@example.com`]);
  fixtures.learners.push(legacyChild.rows[0].id);
  await migrate.client.call("/profile", "PUT", { fullName: "Duplicate", role: "parent" }, 409);
  const code = randomUUID() + randomUUID();
  const hash = createHash("sha256").update(code).digest("hex").toUpperCase();
  await pool.query("INSERT INTO guru_identity_invitations(code_hash,profile_id,email,expires_at) VALUES($1,$2,$3,now()+interval '1 day')", [hash, legacyId, migrate.email]);
  await parent.client.call("/auth/migrate", "POST", { code }, 400);
  await migrate.client.call("/auth/migrate", "POST", { code });
  ok((await migrate.client.call("/profile")).id===legacyId, "Migration preserves original profile ID");
  ok((await migrate.client.call("/learners")).some(l=>l.id===legacyChild.rows[0].id), "Migration preserves parent–learner ownership");
  await migrate.client.call("/auth/migrate", "POST", { code }, 400);
  await pool.query("UPDATE guru_enrollments SET payment_status='awaiting_provider',status='pending' WHERE id=$1", [enrollment.id]);
  await parent.client.call(`/enrollments/${enrollment.id}`, "PATCH", { action: "cancel" });
  ok((await parent.client.call("/enrollments")).some(e=>e.id===enrollment.id && e.status==="cancelled"), "Pending cancellation persists");
  await parent.client.call("/auth/forgot-password", "POST", { email: parent.email });
  const { rows: [reset] } = await pool.query("SELECT link FROM guru_identity_dev_mail WHERE recipient=$1 AND subject LIKE 'Reset%' ORDER BY created_at DESC LIMIT 1", [parent.email]);
  const resetUrl = new URL(reset.link);
  const newPassword = password + "Changed!";
  await parent.client.call("/auth/reset-password", "POST", { userId: resetUrl.searchParams.get("userId"), token: resetUrl.searchParams.get("token"), password: newPassword });
  await parent.client.call("/auth/logout", "POST", undefined, 204);
  await parent.client.call("/auth/login", "POST", { email: parent.email, password }, 401);
  await parent.client.call("/auth/login", "POST", { email: parent.email, password: newPassword });
  await parent.client.call("/auth/reset-password", "POST", { userId: resetUrl.searchParams.get("userId"), token: resetUrl.searchParams.get("token"), password: newPassword }, 400);
  console.log(`PASS: ${checks} API checks, including roles, migration ownership, billing gates, storage ranges, verification and recovery.`);
  completed = true;
  if (process.env.KEEP_TEST_FIXTURES === "1") {
    await mkdir(".data", { recursive: true });
    await writeFile(".data/browser-fixtures.json", JSON.stringify({ origin, prefix, password, parent: {email:parent.email,password:newPassword}, tutor:{email:tutor.email,password},learner:{email:learner.email,password}, fixtures, users }), { mode:0o600 });
    console.log("Browser test fixtures retained privately in .data/browser-fixtures.json.");
  }
} finally {
  if (!completed || process.env.KEEP_TEST_FIXTURES !== "1") await cleanup();
  await pool.end();
}
