import { pool } from "@workspace/db";
import { getAuth } from "@clerk/express";
import type { Request, Response, NextFunction } from "express";

export type ProfileRow = { id: string; fullName: string; email: string; role: string; phone: string | null; grade: number | null; createdAt: Date };
declare global {
  namespace Express {
    interface Request { guruUserId?: string; guruProfile?: ProfileRow }
  }
}
export class HttpError extends Error {
  constructor(public status: number, message: string) { super(message); }
}
export async function query(sql: string, params: unknown[] = []) {
  return (await pool.query(sql, params)).rows;
}
export const profileSelect = `SELECT id, full_name AS "fullName", email, role, phone, grade, created_at AS "createdAt" FROM guru_profiles`;
export async function requireUser(req: Request, res: Response, next: NextFunction) {
  const userId = getAuth(req).userId;
  if (!userId) { res.status(401).json({ error: "Please sign in to continue." }); return; }
  req.guruUserId = userId;
  next();
}
export async function requireProfile(req: Request, res: Response, next: NextFunction) {
  const [profile] = await query(`${profileSelect} WHERE id=$1`, [req.guruUserId]);
  if (!profile) { res.status(409).json({ error: "Complete your account setup first." }); return; }
  req.guruProfile = profile as ProfileRow;
  next();
}
export function requireRole(req: Request, role: string) {
  if (req.guruProfile?.role !== role) throw new HttpError(403, `This action is available to ${role}s only.`);
}
export async function accessibleCourses(req: Request): Promise<string[]> {
  const p = req.guruProfile!;
  if (p.role === "tutor") return (await query("SELECT id FROM guru_courses WHERE tutor_id=$1", [p.id])).map(r => r.id);
  const rows = await query(`
    SELECT DISTINCT c.id FROM guru_courses c
    JOIN guru_enrollments e ON c.subject_id = ANY(e.subject_ids)
    JOIN guru_learners l ON l.id=e.learner_id AND l.grade BETWEEN c.grade_min AND c.grade_max
    WHERE e.status IN ('active','cancelling') AND e.payment_status='paid' AND e.period_ends_at>now()
    AND ${p.role === "parent" ? "e.parent_id=$1" : "lower(l.email)=lower($1)"}
  `, [p.role === "parent" ? p.id : p.email]);
  return rows.map(r => r.id);
}
export async function assertCourseOwner(req: Request, courseId: string) {
  requireRole(req, "tutor");
  const [course] = await query("SELECT * FROM guru_courses WHERE id=$1 AND tutor_id=$2", [courseId, req.guruUserId]);
  if (!course) throw new HttpError(404, "Course not found.");
  return course;
}
export async function eligibleLearners(req: Request) {
  const p = req.guruProfile!;
  if (p.role === "parent") return query(`SELECT id,name,email,grade,created_at AS "createdAt" FROM guru_learners WHERE parent_id=$1 ORDER BY created_at DESC`, [p.id]);
  if (p.role === "learner") return query(`SELECT id,name,email,grade,created_at AS "createdAt" FROM guru_learners WHERE lower(email)=lower($1)`, [p.email]);
  return query(`SELECT DISTINCT l.id,l.name,l.email,l.grade,l.created_at AS "createdAt" FROM guru_learners l JOIN guru_enrollments e ON e.learner_id=l.id JOIN guru_courses c ON c.subject_id=ANY(e.subject_ids) AND l.grade BETWEEN c.grade_min AND c.grade_max WHERE c.tutor_id=$1 AND e.payment_status='paid' AND e.status IN ('active','cancelling') AND e.period_ends_at>now()`, [p.id]);
}
export const courseSelect = `SELECT c.id,c.subject_id AS "subjectId",s.name AS "subjectName",c.title,c.grade_min AS "gradeMin",c.grade_max AS "gradeMax",p.full_name AS "tutorName",c.schedule_summary AS "scheduleSummary" FROM guru_courses c JOIN guru_subjects s ON s.id=c.subject_id JOIN guru_profiles p ON p.id=c.tutor_id`;
export const sessionSelect = `SELECT ss.id,ss.course_id AS "courseId",c.title AS "courseTitle",s.name AS "subjectName",ss.title,ss.starts_at AS "startsAt",ss.duration_minutes AS "durationMinutes",ss.status,ss.meeting_url AS "meetingUrl",ss.lesson_plan AS "lessonPlan",p.full_name AS "tutorName",(SELECT count(*)::int FROM guru_attendance a WHERE a.session_id=ss.id AND a.attended) AS "attendanceCount" FROM guru_sessions ss JOIN guru_courses c ON c.id=ss.course_id JOIN guru_subjects s ON s.id=c.subject_id JOIN guru_profiles p ON p.id=c.tutor_id`;
export const enrollmentSelect = `SELECT e.id,e.learner_id AS "learnerId",l.name AS "learnerName",l.grade AS "learnerGrade",e.subject_ids AS "subjectIds",e.monthly_amount_zar AS "monthlyAmountZar",e.status,e.payment_status AS "paymentStatus",e.period_ends_at AS "periodEndsAt",e.created_at AS "createdAt" FROM guru_enrollments e JOIN guru_learners l ON l.id=e.learner_id`;
export async function validateSubjects(subjectIds: string[], grade: number) {
  if (new Set(subjectIds).size !== subjectIds.length || subjectIds.length < 1 || subjectIds.length > 2) throw new HttpError(400, "Choose one or two different subjects.");
  const rows = await query("SELECT id FROM guru_subjects WHERE id=ANY($1::text[]) AND $2=ANY(grades)", [subjectIds, grade]);
  if (rows.length !== subjectIds.length) throw new HttpError(400, "One or more selected subjects are not available for this grade.");
}
