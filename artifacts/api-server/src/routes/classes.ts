import { Router } from "express";
import {
  ListSessionsQueryParams, ListSessionsResponse, CreateSessionBody, CreateSessionResponse,
  UpdateSessionParams, UpdateSessionBody, UpdateSessionResponse, MarkAttendanceParams,
  MarkAttendanceBody, MarkAttendanceResponse, ListProgressQueryParams, ListProgressResponse,
  GetOverviewResponse,
} from "@workspace/api-zod";
import { query, sessionSelect, enrollmentSelect, requireUser, requireProfile, accessibleCourses, assertCourseOwner, eligibleLearners, requireRole, HttpError } from "../lib/tutoring";

const router = Router();
router.use(requireUser, requireProfile);
function checkMeetingLink(value: string | null | undefined) {
  if (!value) return;
  const url = new URL(value);
  const host = url.hostname.toLowerCase();
  if (url.protocol !== "https:" || !(host === "teams.microsoft.com" || host.endsWith(".teams.microsoft.com") || host === "teams.live.com" || host === "zoom.us" || host.endsWith(".zoom.us"))) {
    throw new HttpError(400, "Use a secure Microsoft Teams or Zoom meeting link.");
  }
}
router.get("/sessions", async (req, res) => {
  const params = ListSessionsQueryParams.parse(req.query);
  const ids = await accessibleCourses(req);
  const rows = await query(`${sessionSelect} WHERE ss.course_id=ANY($1::uuid[]) AND ($2::uuid IS NULL OR ss.course_id=$2) ORDER BY ss.starts_at DESC`, [ids, params.courseId ?? null]);
  if (req.guruProfile!.role !== "tutor") for (const row of rows) row.lessonPlan = null;
  res.json(ListSessionsResponse.parse(rows));
});
router.post("/sessions", async (req, res) => {
  const data = CreateSessionBody.parse(req.body);
  await assertCourseOwner(req, data.courseId);
  checkMeetingLink(data.meetingUrl);
  if (data.durationMinutes !== 60) throw new HttpError(400, "TheTutorGuru live classes are one hour long.");
  const [row] = await query(`INSERT INTO guru_sessions(course_id,title,starts_at,duration_minutes,meeting_url,lesson_plan) VALUES($1,$2,$3,60,$4,$5) RETURNING id`, [data.courseId, data.title.trim(), data.startsAt, data.meetingUrl ?? null, data.lessonPlan ?? null]);
  const [session] = await query(`${sessionSelect} WHERE ss.id=$1`, [row.id]);
  res.status(201).json(CreateSessionResponse.parse(session));
});
router.patch("/sessions/:sessionId", async (req, res) => {
  const { sessionId } = UpdateSessionParams.parse(req.params);
  const data = UpdateSessionBody.parse(req.body);
  const [session] = await query("SELECT course_id,meeting_url FROM guru_sessions WHERE id=$1", [sessionId]);
  if (!session) throw new HttpError(404, "Session not found.");
  await assertCourseOwner(req, session.course_id);
  checkMeetingLink(data.meetingUrl);
  if (data.durationMinutes !== undefined && data.durationMinutes !== 60) throw new HttpError(400, "Live classes must last one hour.");
  if (data.status === "live" && !(data.meetingUrl === undefined ? session.meeting_url : data.meetingUrl)) throw new HttpError(400, "Set the meeting link before starting a class.");
  const columns: Record<string, string> = { title: "title", startsAt: "starts_at", durationMinutes: "duration_minutes", status: "status", meetingUrl: "meeting_url", lessonPlan: "lesson_plan" };
  const entries = Object.entries(data).filter(([,v]) => v !== undefined);
  if (entries.length) await query(`UPDATE guru_sessions SET ${entries.map(([k],i) => `${columns[k]}=$${i+1}`).join(",")} WHERE id=$${entries.length+1}`, [...entries.map(([,v]) => v), sessionId]);
  const [updated] = await query(`${sessionSelect} WHERE ss.id=$1`, [sessionId]);
  res.json(UpdateSessionResponse.parse(updated));
});
router.put("/attendance/:sessionId/:learnerId", async (req, res) => {
  requireRole(req, "tutor");
  const { sessionId, learnerId } = MarkAttendanceParams.parse(req.params);
  const data = MarkAttendanceBody.parse(req.body);
  const [session] = await query("SELECT course_id FROM guru_sessions WHERE id=$1", [sessionId]);
  if (!session) throw new HttpError(404, "Session not found.");
  const course = await assertCourseOwner(req, session.course_id);
  const [learner] = await query(`SELECT l.id FROM guru_learners l JOIN guru_enrollments e ON e.learner_id=l.id WHERE l.id=$1 AND $2=ANY(e.subject_ids) AND l.grade BETWEEN $3 AND $4 AND e.payment_status='paid' AND e.status IN ('active','cancelling') AND e.period_ends_at>now()`, [learnerId, course.subject_id, course.grade_min, course.grade_max]);
  if (!learner) throw new HttpError(403, "This learner is not enrolled in this course.");
  const [attendance] = await query(`INSERT INTO guru_attendance(session_id,learner_id,attended) VALUES($1,$2,$3) ON CONFLICT(session_id,learner_id) DO UPDATE SET attended=EXCLUDED.attended,marked_at=now() RETURNING session_id AS "sessionId",learner_id AS "learnerId",attended,marked_at AS "markedAt"`, [sessionId, learnerId, data.attended]);
  res.json(MarkAttendanceResponse.parse(attendance));
});
router.get("/progress", async (req, res) => {
  const params = ListProgressQueryParams.parse(req.query);
  const learners = await eligibleLearners(req);
  const learnerIds = learners.map(l => l.id);
  const ids = await accessibleCourses(req);
  const rows = await query(`
    SELECT l.id AS "learnerId",l.name AS "learnerName",l.grade,s.id AS "subjectId",s.name AS "subjectName",
    count(DISTINCT ss.id) FILTER(WHERE ss.status='completed')::int AS "classesTotal",
    count(DISTINCT a.session_id) FILTER(WHERE a.attended AND ss.status='completed')::int AS "classesAttended",
    CASE WHEN count(DISTINCT ss.id) FILTER(WHERE ss.status='completed')=0 THEN 0 ELSE round(100.0*count(DISTINCT a.session_id) FILTER(WHERE a.attended AND ss.status='completed') / count(DISTINCT ss.id) FILTER(WHERE ss.status='completed'))::int END AS "attendancePercent",
    NULL::numeric AS "averageScore",coalesce(max(a.marked_at),l.created_at) AS "updatedAt"
    FROM guru_learners l JOIN guru_enrollments e ON e.learner_id=l.id
    JOIN guru_subjects s ON s.id=ANY(e.subject_ids)
    JOIN guru_courses c ON c.subject_id=s.id AND l.grade BETWEEN c.grade_min AND c.grade_max
    LEFT JOIN guru_sessions ss ON ss.course_id=c.id
    LEFT JOIN guru_attendance a ON a.session_id=ss.id AND a.learner_id=l.id
    WHERE l.id=ANY($1::uuid[]) AND c.id=ANY($2::uuid[]) AND ($3::uuid IS NULL OR l.id=$3)
    AND e.payment_status='paid' AND e.status IN ('active','cancelling')
    GROUP BY l.id,s.id ORDER BY l.name,s.name`, [learnerIds, ids, params.learnerId ?? null]);
  res.json(ListProgressResponse.parse(rows));
});
router.get("/overview", async (req, res) => {
  const p = req.guruProfile!;
  const ids = await accessibleCourses(req);
  const learners = await eligibleLearners(req);
  const [counts] = await query(`SELECT (SELECT count(*)::int FROM guru_sessions WHERE course_id=ANY($1::uuid[]) AND starts_at>=now() AND status IN ('scheduled','live')) AS upcoming, (SELECT count(DISTINCT subject_id)::int FROM guru_courses WHERE id=ANY($1::uuid[])) AS subjects`, [ids]);
  const sessions = await query(`${sessionSelect} WHERE ss.course_id=ANY($1::uuid[]) ORDER BY ss.starts_at DESC LIMIT 3`, [ids]);
  const enrollments = p.role === "parent" ? await query(`${enrollmentSelect} WHERE e.parent_id=$1 ORDER BY e.created_at DESC LIMIT 3`, [p.id]) : [];
  const recentActivity = [
    ...enrollments.map(e => ({ id:e.id,title:e.status === "pending" ? "Subscription requested" : "Subscription updated", detail:`${e.learnerName} · ${e.subjectIds.length} subject${e.subjectIds.length>1?"s":""} · R${e.monthlyAmountZar}/month`,createdAt:e.createdAt })),
    ...sessions.map(s => ({id:s.id,title:s.title,detail:`${s.subjectName} · ${s.status}`,createdAt:s.startsAt})),
    { id:"account",title:"Welcome to TheTutorGuru",detail:"Your tutoring account is ready.",createdAt:p.createdAt },
  ].sort((a,b) => new Date(b.createdAt).getTime()-new Date(a.createdAt).getTime()).slice(0,5);
  res.json(GetOverviewResponse.parse({ role:p.role, upcomingSessions:counts.upcoming,activeSubjects:counts.subjects,totalLearners:learners.length,recentActivity }));
});
export default router;
