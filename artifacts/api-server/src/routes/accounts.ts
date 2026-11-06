import { Router } from "express";
import { clerkClient } from "@clerk/express";
import {
  SaveProfileBody, SaveProfileResponse, GetProfileResponse, ListSubjectsResponse,
  CreateCourseBody, CreateCourseResponse, ListCoursesQueryParams, ListCoursesResponse,
  ListLearnersResponse, CreateLearnerBody, CreateLearnerResponse,
  ListEnrollmentsResponse, CreateEnrollmentBody, CreateEnrollmentResponse,
  UpdateEnrollmentParams, UpdateEnrollmentBody, UpdateEnrollmentResponse,
} from "@workspace/api-zod";
import { query, profileSelect, courseSelect, enrollmentSelect, requireUser, requireProfile, requireRole, HttpError, eligibleLearners, validateSubjects } from "../lib/tutoring";

const router = Router();
router.get("/subjects", async (_req, res) => {
  res.json(ListSubjectsResponse.parse(await query(`SELECT id,name,grades,description,price_monthly_zar AS "priceMonthlyZar" FROM guru_subjects ORDER BY CASE id WHEN 'mathematics' THEN 1 WHEN 'maths-literacy' THEN 2 WHEN 'physical-science' THEN 3 ELSE 4 END`)));
});
router.get("/courses", async (req, res) => {
  const params = ListCoursesQueryParams.parse(req.query);
  const { getAuth } = await import("@clerk/express");
  const uid = getAuth(req).userId;
  const [profile] = uid ? await query(`${profileSelect} WHERE id=$1`, [uid]) : [];
  const rows = await query(`${courseSelect} WHERE ($1::text IS NULL OR c.subject_id=$1) AND ($2::int IS NULL OR $2 BETWEEN c.grade_min AND c.grade_max) AND ($3::text IS NULL OR c.tutor_id=$3) ORDER BY c.title`, [params.subjectId ?? null, params.grade ?? null, profile?.role === "tutor" ? uid : null]);
  res.json(ListCoursesResponse.parse(rows));
});
router.use(requireUser);
router.get("/profile", async (req, res) => {
  const [profile] = await query(`${profileSelect} WHERE id=$1`, [req.guruUserId]);
  if (!profile) { res.status(404).json({ error: "Create your tutoring profile." }); return; }
  res.json(GetProfileResponse.parse(profile));
});
router.put("/profile", async (req, res) => {
  const data = SaveProfileBody.parse(req.body);
  const [existing] = await query(`${profileSelect} WHERE id=$1`, [req.guruUserId]);
  if (data.role === "admin" && existing?.role !== "admin") throw new HttpError(403, "Administrator access cannot be chosen during account setup.");
  if (existing && existing.role !== data.role) throw new HttpError(409, "Your account role cannot be changed after setup.");
  if (data.role === "learner" && data.grade == null) throw new HttpError(400, "Choose your school grade.");
  const user = await clerkClient.users.getUser(req.guruUserId!);
  const email = user.emailAddresses.find(e => e.id === user.primaryEmailAddressId)?.emailAddress;
  if (!email) throw new HttpError(400, "Your account needs a verified email address.");
  await query(`INSERT INTO guru_profiles(id,full_name,email,role,phone,grade) VALUES($1,$2,$3,$4,$5,$6) ON CONFLICT(id) DO UPDATE SET full_name=EXCLUDED.full_name,email=EXCLUDED.email,phone=EXCLUDED.phone,grade=EXCLUDED.grade`, [req.guruUserId, data.fullName.trim(), email.toLowerCase(), data.role, data.phone ?? null, data.grade ?? null]);
  const [profile] = await query(`${profileSelect} WHERE id=$1`, [req.guruUserId]);
  res.json(SaveProfileResponse.parse(profile));
});
router.use(requireProfile);
router.post("/courses", async (req, res) => {
  requireRole(req, "tutor");
  const data = CreateCourseBody.parse(req.body);
  if (data.gradeMin > data.gradeMax) throw new HttpError(400, "The minimum grade cannot exceed the maximum grade.");
  const [subject] = await query("SELECT grades FROM guru_subjects WHERE id=$1", [data.subjectId]);
  if (!subject || !subject.grades.includes(data.gradeMin) || !subject.grades.includes(data.gradeMax)) throw new HttpError(400, "Select grades offered for this subject.");
  const [row] = await query(`INSERT INTO guru_courses(subject_id,title,grade_min,grade_max,tutor_id,schedule_summary) VALUES($1,$2,$3,$4,$5,$6) RETURNING id`, [data.subjectId, data.title.trim(), data.gradeMin, data.gradeMax, req.guruUserId, data.scheduleSummary ?? null]);
  const [course] = await query(`${courseSelect} WHERE c.id=$1`, [row.id]);
  res.status(201).json(CreateCourseResponse.parse(course));
});
router.get("/learners", async (req, res) => res.json(ListLearnersResponse.parse(await eligibleLearners(req))));
router.post("/learners", async (req, res) => {
  requireRole(req, "parent");
  const data = CreateLearnerBody.parse(req.body);
  const [learner] = await query(`INSERT INTO guru_learners(parent_id,name,email,grade) VALUES($1,$2,$3,$4) RETURNING id,name,email,grade,created_at AS "createdAt"`, [req.guruUserId, data.name.trim(), data.email.toLowerCase(), data.grade]);
  res.status(201).json(CreateLearnerResponse.parse(learner));
});
router.get("/enrollments", async (req, res) => {
  const p = req.guruProfile!;
  const rows = p.role === "parent"
    ? await query(`${enrollmentSelect} WHERE e.parent_id=$1 ORDER BY e.created_at DESC`, [p.id])
    : p.role === "learner" ? await query(`${enrollmentSelect} WHERE lower(l.email)=lower($1) ORDER BY e.created_at DESC`, [p.email]) : [];
  res.json(ListEnrollmentsResponse.parse(rows));
});
router.post("/enrollments", async (req, res) => {
  requireRole(req, "parent");
  const data = CreateEnrollmentBody.parse(req.body);
  const [learner] = await query("SELECT * FROM guru_learners WHERE id=$1 AND parent_id=$2", [data.learnerId, req.guruUserId]);
  if (!learner) throw new HttpError(404, "Learner not found.");
  await validateSubjects(data.subjectIds, learner.grade);
  const [existing] = await query("SELECT id FROM guru_enrollments WHERE learner_id=$1 AND status!='cancelled'", [learner.id]);
  if (existing) throw new HttpError(409, "This learner already has a subscription request. Manage the existing request instead.");
  const [row] = await query(`INSERT INTO guru_enrollments(parent_id,learner_id,subject_ids,monthly_amount_zar) VALUES($1,$2,$3,$4) RETURNING id`, [req.guruUserId, learner.id, data.subjectIds, data.subjectIds.length === 1 ? 290 : 495]);
  const [enrollment] = await query(`${enrollmentSelect} WHERE e.id=$1`, [row.id]);
  res.status(201).json(CreateEnrollmentResponse.parse(enrollment));
});
router.patch("/enrollments/:enrollmentId", async (req, res) => {
  requireRole(req, "parent");
  const { enrollmentId } = UpdateEnrollmentParams.parse(req.params);
  const data = UpdateEnrollmentBody.parse(req.body);
  const [e] = await query("SELECT e.*,l.grade FROM guru_enrollments e JOIN guru_learners l ON l.id=e.learner_id WHERE e.id=$1 AND e.parent_id=$2", [enrollmentId, req.guruUserId]);
  if (!e) throw new HttpError(404, "Subscription not found.");
  if (e.payment_status === "paid") throw new HttpError(409, "Paid subscription management requires the payment gateway to be connected. No billing change has been made.");
  if (data.action === "change") {
    if (e.payment_status === "paid") throw new HttpError(409, "Paid plan changes require the payment gateway to be connected.");
    if (!data.subjectIds) throw new HttpError(400, "Choose one or two subjects.");
    await validateSubjects(data.subjectIds, e.grade);
    await query("UPDATE guru_enrollments SET subject_ids=$1,monthly_amount_zar=$2 WHERE id=$3", [data.subjectIds, data.subjectIds.length === 1 ? 290 : 495, enrollmentId]);
  } else if (data.action === "cancel") {
    await query("UPDATE guru_enrollments SET status=$1 WHERE id=$2", [e.payment_status === "paid" && e.period_ends_at > new Date() ? "cancelling" : "cancelled", enrollmentId]);
  } else {
    if (e.status !== "cancelling") throw new HttpError(409, "Only a subscription scheduled to cancel can be resumed.");
    await query("UPDATE guru_enrollments SET status='active' WHERE id=$1", [enrollmentId]);
  }
  const [updated] = await query(`${enrollmentSelect} WHERE e.id=$1`, [enrollmentId]);
  res.json(UpdateEnrollmentResponse.parse(updated));
});
export default router;
