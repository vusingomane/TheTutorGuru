using System.Text.Json.Nodes;
using TutorGuru.Api.Data;

namespace TutorGuru.Api;

public static class ClassEndpoints
{
    public static void MapClasses(this WebApplication app)
    {
        app.MapGet("/api/sessions", async (HttpContext c, Tutoring domain, Database db, Guid? courseId) =>
        {
            var rows = await db.Query(Tutoring.SessionSelect + " WHERE ss.course_id=ANY($1::uuid[]) AND ($2::uuid IS NULL OR ss.course_id=$2) ORDER BY ss.starts_at DESC", await domain.Accessible(c), courseId);
            if (Tutoring.Role(c) != "tutor") foreach (var r in rows) r["lessonPlan"] = null;
            return Results.Json(rows);
        });
        app.MapPost("/api/sessions", async (SessionInput body, HttpContext c, Tutoring domain, Database db) =>
        {
            await domain.Owner(c, body.CourseId);
            Tutoring.Text(body.Title, "class title"); Tutoring.Meeting(body.MeetingUrl);
            if (body.DurationMinutes != 60) throw new HttpError(400, "TheTutorGuru live classes are one hour long.");
            var row = await db.One("INSERT INTO guru_sessions(course_id,title,starts_at,duration_minutes,meeting_url,lesson_plan) VALUES($1,$2,$3,60,$4,$5) RETURNING id", body.CourseId, body.Title.Trim(), body.StartsAt.ToUniversalTime(), body.MeetingUrl, body.LessonPlan);
            return Results.Json(await db.One(Tutoring.SessionSelect + " WHERE ss.id=$1", row!["id"]), statusCode: 201);
        });
        app.MapPatch("/api/sessions/{sessionId:guid}", async (Guid sessionId, JsonObject body, HttpContext c, Tutoring domain, Database db) =>
        {
            var session = await db.One("SELECT course_id,meeting_url FROM guru_sessions WHERE id=$1", sessionId) ?? throw new HttpError(404, "Session not found.");
            await domain.Owner(c, (Guid)session["course_id"]!);
            Dictionary<string, string> columns = new() { ["title"] = "title", ["startsAt"] = "starts_at", ["durationMinutes"] = "duration_minutes", ["status"] = "status", ["meetingUrl"] = "meeting_url", ["lessonPlan"] = "lesson_plan" };
            var entries = body.Where(kv => columns.ContainsKey(kv.Key)).ToArray();
            if (body.ContainsKey("title")) Tutoring.Text(body["title"]?.GetValue<string>(), "class title");
            if (body.ContainsKey("meetingUrl")) Tutoring.Meeting(body["meetingUrl"]?.GetValue<string>());
            if (body.ContainsKey("durationMinutes") && body["durationMinutes"]?.GetValue<int>() != 60) throw new HttpError(400, "Live classes must last one hour.");
            if (body.ContainsKey("status"))
            {
                var status = body["status"]?.GetValue<string>();
                if (!new[] { "scheduled", "live", "completed", "cancelled" }.Contains(status)) throw new HttpError(400, "Choose a valid class status.");
                if (status == "live" && string.IsNullOrWhiteSpace(body.ContainsKey("meetingUrl") ? body["meetingUrl"]?.GetValue<string>() : session["meeting_url"]?.ToString())) throw new HttpError(400, "Set the meeting link before starting a class.");
            }
            if (entries.Length > 0)
            {
                var args = entries.Select(kv => kv.Key switch
                {
                    "startsAt" => (object)(kv.Value?.GetValue<DateTimeOffset>().ToUniversalTime() ?? throw new HttpError(400, "Choose a valid start time.")),
                    "durationMinutes" => kv.Value?.GetValue<int>(),
                    _ => (object?)kv.Value?.GetValue<string>()
                }).Append(sessionId).ToArray();
                await db.Query($"UPDATE guru_sessions SET {string.Join(",", entries.Select((kv, i) => $"{columns[kv.Key]}=${i + 1}"))} WHERE id=${args.Length}", args);
            }
            return Results.Json(await db.One(Tutoring.SessionSelect + " WHERE ss.id=$1", sessionId));
        });
        app.MapPut("/api/attendance/{sessionId:guid}/{learnerId:guid}", async (Guid sessionId, Guid learnerId, AttendanceInput body, HttpContext c, Tutoring domain, Database db) =>
        {
            var ss = await db.One("SELECT course_id FROM guru_sessions WHERE id=$1", sessionId) ?? throw new HttpError(404, "Session not found.");
            var course = await domain.Owner(c, (Guid)ss["course_id"]!);
            if (await db.One("SELECT l.id FROM guru_learners l JOIN guru_enrollments e ON e.learner_id=l.id WHERE l.id=$1 AND $2=ANY(e.subject_ids) AND l.grade BETWEEN $3 AND $4 AND e.payment_status='paid' AND e.status IN ('active','cancelling') AND e.period_ends_at>now()" + domain.PaymentEnvironmentFilter, learnerId, course["subject_id"], course["grade_min"], course["grade_max"]) is null)
                throw new HttpError(403, "This learner is not enrolled in this course.");
            return Results.Json(await db.One("""INSERT INTO guru_attendance(session_id,learner_id,attended) VALUES($1,$2,$3) ON CONFLICT(session_id,learner_id) DO UPDATE SET attended=EXCLUDED.attended,marked_at=now() RETURNING session_id AS "sessionId",learner_id AS "learnerId",attended,marked_at AS "markedAt" """, sessionId, learnerId, body.Attended));
        });
        app.MapGet("/api/progress", async (Guid? learnerId, HttpContext c, Tutoring domain, Database db) =>
        {
            var learners = (await domain.Learners(c)).Select(l => (Guid)l["id"]!).ToArray();
            return Results.Json(await db.Query("""
                SELECT l.id AS "learnerId",l.name AS "learnerName",l.grade,s.id AS "subjectId",s.name AS "subjectName",
                count(DISTINCT ss.id) FILTER(WHERE ss.status='completed')::int AS "classesTotal",
                count(DISTINCT a.session_id) FILTER(WHERE a.attended AND ss.status='completed')::int AS "classesAttended",
                CASE WHEN count(DISTINCT ss.id) FILTER(WHERE ss.status='completed')=0 THEN 0 ELSE round(100.0*count(DISTINCT a.session_id) FILTER(WHERE a.attended AND ss.status='completed') / count(DISTINCT ss.id) FILTER(WHERE ss.status='completed'))::int END AS "attendancePercent",
                NULL::numeric AS "averageScore",coalesce(max(a.marked_at),l.created_at) AS "updatedAt"
                FROM guru_learners l JOIN guru_enrollments e ON e.learner_id=l.id
                JOIN guru_subjects s ON s.id=ANY(e.subject_ids)
                JOIN guru_courses c ON c.subject_id=s.id AND l.grade BETWEEN c.grade_min AND c.grade_max
                LEFT JOIN guru_sessions ss ON ss.course_id=c.id LEFT JOIN guru_attendance a ON a.session_id=ss.id AND a.learner_id=l.id
                WHERE l.id=ANY($1::uuid[]) AND c.id=ANY($2::uuid[]) AND ($3::uuid IS NULL OR l.id=$3)
                AND e.payment_status='paid' AND e.status IN ('active','cancelling') AND e.period_ends_at>now()
                GROUP BY l.id,s.id ORDER BY l.name,s.name
                """, learners, await domain.Accessible(c), learnerId));
        });
        app.MapGet("/api/overview", async (HttpContext c, Tutoring domain, Database db) =>
        {
            var ids = await domain.Accessible(c);
            var learners = await domain.Learners(c);
            var counts = await db.One("""SELECT (SELECT count(*)::int FROM guru_sessions WHERE course_id=ANY($1::uuid[]) AND starts_at>=now() AND status IN ('scheduled','live')) AS upcoming,(SELECT count(DISTINCT subject_id)::int FROM guru_courses WHERE id=ANY($1::uuid[])) AS subjects""", (object)ids);
            var sessions = await db.Query(Tutoring.SessionSelect + " WHERE ss.course_id=ANY($1::uuid[]) ORDER BY ss.starts_at DESC LIMIT 3", (object)ids);
            var enrollments = Tutoring.Role(c) == "parent" ? await db.Query(Tutoring.EnrollmentSelect + " WHERE e.parent_id=$1 ORDER BY e.created_at DESC LIMIT 3", Tutoring.Id(c)) : [];
            var activity = enrollments.Select(e => new Activity(e["id"]!.ToString()!, e["status"]?.ToString() == "pending" ? "Subscription requested" : "Subscription updated", $"{e["learnerName"]} · {((string[])e["subjectIds"]!).Length} subject(s) · R{e["monthlyAmountZar"]}/month", (DateTime)e["createdAt"]!))
                .Concat(sessions.Select(s => new Activity(s["id"]!.ToString()!, s["title"]!.ToString()!, $"{s["subjectName"]} · {s["status"]}", (DateTime)s["startsAt"]!)))
                .Append(new Activity("account", "Welcome to TheTutorGuru", "Your tutoring account is ready.", (DateTime)Tutoring.Profile(c)["createdAt"]!))
                .OrderByDescending(a => a.CreatedAt).Take(5);
            return Results.Json(new { role = Tutoring.Role(c), upcomingSessions = counts!["upcoming"], activeSubjects = counts["subjects"], totalLearners = learners.Count, recentActivity = activity });
        });
    }
    private record Activity(string Id, string Title, string Detail, DateTime CreatedAt);
}
public record SessionInput(Guid CourseId, string Title, DateTimeOffset StartsAt, int DurationMinutes, string? MeetingUrl, string? LessonPlan);
public record AttendanceInput(bool Attended);
