import { pgTable, text, uuid, integer, timestamp, boolean, uniqueIndex } from "drizzle-orm/pg-core";
import { sql } from "drizzle-orm";

export const guruProfiles = pgTable("guru_profiles", {
  id: text("id").primaryKey(),
  fullName: text("full_name").notNull(),
  email: text("email").notNull(),
  role: text("role").notNull(),
  phone: text("phone"),
  grade: integer("grade"),
  createdAt: timestamp("created_at", { withTimezone: true }).notNull().defaultNow(),
});
export const guruSubjects = pgTable("guru_subjects", {
  id: text("id").primaryKey(),
  name: text("name").notNull(),
  grades: integer("grades").array().notNull(),
  description: text("description").notNull(),
  priceMonthlyZar: integer("price_monthly_zar").notNull().default(290),
});
export const guruCourses = pgTable("guru_courses", {
  id: uuid("id").primaryKey().defaultRandom(),
  subjectId: text("subject_id").notNull().references(() => guruSubjects.id),
  title: text("title").notNull(),
  gradeMin: integer("grade_min").notNull(),
  gradeMax: integer("grade_max").notNull(),
  tutorId: text("tutor_id").notNull().references(() => guruProfiles.id),
  scheduleSummary: text("schedule_summary"),
});
export const guruLearners = pgTable("guru_learners", {
  id: uuid("id").primaryKey().defaultRandom(),
  parentId: text("parent_id").notNull().references(() => guruProfiles.id),
  name: text("name").notNull(),
  email: text("email").notNull(),
  grade: integer("grade").notNull(),
  createdAt: timestamp("created_at", { withTimezone: true }).notNull().defaultNow(),
}, (t) => [uniqueIndex("guru_learner_parent_email").on(t.parentId, t.email)]);
export const guruEnrollments = pgTable("guru_enrollments", {
  id: uuid("id").primaryKey().defaultRandom(),
  parentId: text("parent_id").notNull().references(() => guruProfiles.id),
  learnerId: uuid("learner_id").notNull().references(() => guruLearners.id),
  subjectIds: text("subject_ids").array().notNull(),
  monthlyAmountZar: integer("monthly_amount_zar").notNull(),
  status: text("status").notNull().default("pending"),
  paymentStatus: text("payment_status").notNull().default("awaiting_provider"),
  periodEndsAt: timestamp("period_ends_at", { withTimezone: true }),
  createdAt: timestamp("created_at", { withTimezone: true }).notNull().defaultNow(),
}, (t) => [uniqueIndex("guru_enrollment_current_learner").on(t.learnerId).where(sql`${t.status} <> 'cancelled'`)]);
export const guruSessions = pgTable("guru_sessions", {
  id: uuid("id").primaryKey().defaultRandom(),
  courseId: uuid("course_id").notNull().references(() => guruCourses.id),
  title: text("title").notNull(),
  startsAt: timestamp("starts_at", { withTimezone: true }).notNull(),
  durationMinutes: integer("duration_minutes").notNull().default(60),
  status: text("status").notNull().default("scheduled"),
  meetingUrl: text("meeting_url"),
  lessonPlan: text("lesson_plan"),
});
export const guruMaterials = pgTable("guru_materials", {
  id: uuid("id").primaryKey().defaultRandom(),
  courseId: uuid("course_id").notNull().references(() => guruCourses.id),
  ownerId: text("owner_id").notNull().references(() => guruProfiles.id),
  title: text("title").notNull(),
  originalFileName: text("original_file_name").notNull(),
  objectPath: text("object_path").notNull(),
  contentType: text("content_type").notNull(),
  sizeBytes: integer("size_bytes").notNull(),
  uploadedAt: timestamp("uploaded_at", { withTimezone: true }).notNull().defaultNow(),
});
export const guruRecordings = pgTable("guru_recordings", {
  id: uuid("id").primaryKey().defaultRandom(),
  sessionId: uuid("session_id").notNull().references(() => guruSessions.id),
  ownerId: text("owner_id").notNull().references(() => guruProfiles.id),
  title: text("title").notNull(),
  objectPath: text("object_path").notNull(),
  durationSeconds: integer("duration_seconds").notNull(),
  uploadedAt: timestamp("uploaded_at", { withTimezone: true }).notNull().defaultNow(),
});
export const guruAttendance = pgTable("guru_attendance", {
  id: uuid("id").primaryKey().defaultRandom(),
  sessionId: uuid("session_id").notNull().references(() => guruSessions.id),
  learnerId: uuid("learner_id").notNull().references(() => guruLearners.id),
  attended: boolean("attended").notNull(),
  markedAt: timestamp("marked_at", { withTimezone: true }).notNull().defaultNow(),
}, (t) => [uniqueIndex("guru_attendance_session_learner").on(t.sessionId, t.learnerId)]);
export const guruUploads = pgTable("guru_uploads", {
  objectPath: text("object_path").primaryKey(),
  ownerId: text("owner_id").notNull().references(() => guruProfiles.id),
  name: text("name").notNull(),
  size: integer("size").notNull(),
  contentType: text("content_type").notNull(),
  createdAt: timestamp("created_at", { withTimezone: true }).notNull().defaultNow(),
});
