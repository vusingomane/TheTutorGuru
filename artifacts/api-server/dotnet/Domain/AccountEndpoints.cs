using Microsoft.AspNetCore.Identity;
using TutorGuru.Api.Data;

namespace TutorGuru.Api;

public static class AccountEndpoints
{
    public static void MapAccounts(this WebApplication app)
    {
        app.MapGet("/api/subjects", async (Database db) => Results.Json(await db.Query("""SELECT id,name,grades,description,price_monthly_zar AS "priceMonthlyZar" FROM guru_subjects ORDER BY CASE id WHEN 'mathematics' THEN 1 WHEN 'maths-literacy' THEN 2 WHEN 'physical-science' THEN 3 ELSE 4 END""")));
        app.MapGet("/api/courses", async (HttpContext c, Database db, Tutoring domain, string? subjectId, int? grade) =>
        {
            string? tutor = null;
            if (c.User.Identity?.IsAuthenticated == true)
            {
                await domain.Load(c, false);
                if (c.Items.ContainsKey("profile") && Tutoring.Role(c) == "tutor") tutor = Tutoring.Id(c);
            }
            return Results.Json(await db.Query(Tutoring.CourseSelect + " WHERE ($1::text IS NULL OR c.subject_id=$1) AND ($2::int IS NULL OR $2 BETWEEN c.grade_min AND c.grade_max) AND ($3::text IS NULL OR c.tutor_id=$3) ORDER BY c.title", subjectId, grade, tutor));
        });
        app.MapGet("/api/profile", async (HttpContext c, Database db) =>
            Results.Json(await db.One(Tutoring.ProfileSelect + " WHERE id=$1", Tutoring.Id(c)) ?? throw new HttpError(404, "Create your tutoring profile or link your existing account.")));
        app.MapPut("/api/profile", async (ProfileInput body, HttpContext c, Database db, UserManager<IdentityUser> users) =>
        {
            Tutoring.Text(body.FullName, "full name", 2, 200);
            if (!new[] { "parent", "learner", "tutor" }.Contains(body.Role)) throw new HttpError(400, "Choose parent, learner, or tutor.");
            if (body.Role == "learner" && body.Grade is null) throw new HttpError(400, "Choose your school grade.");
            if (body.Grade.HasValue) Tutoring.Grade(body.Grade.Value);
            var user = await users.GetUserAsync(c.User);
            if (user?.EmailConfirmed != true) throw new HttpError(403, "Verify your email before completing your profile.");
            var existing = await db.One(Tutoring.ProfileSelect + " WHERE id=$1", Tutoring.Id(c));
            if (existing is not null && (string)existing["role"]! != body.Role) throw new HttpError(409, "Your account role cannot be changed after setup.");
            // Email alone never transfers ownership of an existing Clerk profile.
            if (existing is null && await db.One("SELECT id FROM guru_profiles WHERE lower(email)=lower($1) AND id<>$2", user.Email, Tutoring.Id(c)) is not null)
                throw new HttpError(409, "An existing tutoring account uses this email. Link it with a migration invitation instead of creating a duplicate profile.");
            await db.Query("INSERT INTO guru_profiles(id,full_name,email,role,phone,grade) VALUES($1,$2,$3,$4,$5,$6) ON CONFLICT(id) DO UPDATE SET full_name=EXCLUDED.full_name,phone=EXCLUDED.phone,grade=EXCLUDED.grade",
                Tutoring.Id(c), body.FullName.Trim(), user.Email!.ToLowerInvariant(), body.Role, body.Phone, body.Grade);
            return Results.Json(await db.One(Tutoring.ProfileSelect + " WHERE id=$1", Tutoring.Id(c)));
        });
        app.MapPost("/api/courses", async (CourseInput body, HttpContext c, Database db) =>
        {
            Tutoring.RequireRole(c, "tutor"); Tutoring.Text(body.Title, "course title");
            Tutoring.Grade(body.GradeMin); Tutoring.Grade(body.GradeMax);
            var subject = await db.One("SELECT grades FROM guru_subjects WHERE id=$1", body.SubjectId);
            var grades = subject?["grades"] as int[];
            if (body.GradeMin > body.GradeMax || grades is null || !grades.Contains(body.GradeMin) || !grades.Contains(body.GradeMax))
                throw new HttpError(400, "Select grades offered for this subject.");
            var row = await db.One("INSERT INTO guru_courses(subject_id,title,grade_min,grade_max,tutor_id,schedule_summary) VALUES($1,$2,$3,$4,$5,$6) RETURNING id", body.SubjectId, body.Title.Trim(), body.GradeMin, body.GradeMax, Tutoring.Id(c), body.ScheduleSummary);
            return Results.Json(await db.One(Tutoring.CourseSelect + " WHERE c.id=$1", row!["id"]), statusCode: 201);
        });
        app.MapGet("/api/learners", async (HttpContext c, Tutoring domain) => Results.Json(await domain.Learners(c)));
        app.MapPost("/api/learners", async (LearnerInput body, HttpContext c, Database db) =>
        {
            Tutoring.RequireRole(c, "parent"); Tutoring.Text(body.Name, "learner name", 2, 200); Tutoring.Grade(body.Grade);
            if (!System.Net.Mail.MailAddress.TryCreate(body.Email, out _)) throw new HttpError(400, "Enter a valid learner email.");
            return Results.Json(await db.One("""INSERT INTO guru_learners(parent_id,name,email,grade) VALUES($1,$2,$3,$4) RETURNING id,name,email,grade,created_at AS "createdAt" """, Tutoring.Id(c), body.Name.Trim(), body.Email.Trim().ToLowerInvariant(), body.Grade), statusCode: 201);
        });
        app.MapGet("/api/enrollments", async (HttpContext c, Database db) =>
        {
            var rows = Tutoring.Role(c) == "parent" ? await db.Query(Tutoring.EnrollmentSelect + " WHERE e.parent_id=$1 ORDER BY e.created_at DESC", Tutoring.Id(c))
                : Tutoring.Role(c) == "learner" ? await db.Query(Tutoring.EnrollmentSelect + " WHERE lower(l.email)=lower($1) ORDER BY e.created_at DESC", Tutoring.Profile(c)["email"])
                : [];
            return Results.Json(rows);
        });
        app.MapPost("/api/enrollments", async (EnrollmentInput body, HttpContext c, Database db, Tutoring domain) =>
        {
            Tutoring.RequireRole(c, "parent");
            var learner = await db.One("SELECT * FROM guru_learners WHERE id=$1 AND parent_id=$2", body.LearnerId, Tutoring.Id(c)) ?? throw new HttpError(404, "Learner not found.");
            await domain.ValidateSubjects(body.SubjectIds, (int)learner["grade"]!);
            var row = await db.One("INSERT INTO guru_enrollments(parent_id,learner_id,subject_ids,monthly_amount_zar) VALUES($1,$2,$3,$4) RETURNING id", Tutoring.Id(c), body.LearnerId, body.SubjectIds, body.SubjectIds.Length == 1 ? 290 : 495);
            return Results.Json(await db.One(Tutoring.EnrollmentSelect + " WHERE e.id=$1", row!["id"]), statusCode: 201);
        });
        app.MapPatch("/api/enrollments/{enrollmentId:guid}", async (Guid enrollmentId, EnrollmentUpdate body, HttpContext c, Database db, Tutoring domain) =>
        {
            Tutoring.RequireRole(c, "parent");
            var e = await db.One("SELECT e.*,l.grade FROM guru_enrollments e JOIN guru_learners l ON l.id=e.learner_id WHERE e.id=$1 AND e.parent_id=$2", enrollmentId, Tutoring.Id(c)) ?? throw new HttpError(404, "Subscription not found.");
            if ((string)e["payment_status"]! == "paid") throw new HttpError(409, "Paid subscription management requires the payment gateway to be connected. No billing change has been made.");
            switch (body.Action)
            {
                case "change":
                    await domain.ValidateSubjects(body.SubjectIds, (int)e["grade"]!);
                    await db.Query("UPDATE guru_enrollments SET subject_ids=$1,monthly_amount_zar=$2 WHERE id=$3", body.SubjectIds, body.SubjectIds!.Length == 1 ? 290 : 495, enrollmentId);
                    break;
                case "cancel": await db.Query("UPDATE guru_enrollments SET status='cancelled' WHERE id=$1", enrollmentId); break;
                case "resume":
                    if ((string)e["status"]! != "cancelling") throw new HttpError(409, "Only a subscription scheduled to cancel can be resumed.");
                    await db.Query("UPDATE guru_enrollments SET status='active' WHERE id=$1", enrollmentId); break;
                default: throw new HttpError(400, "Choose cancel, resume, or change.");
            }
            return Results.Json(await db.One(Tutoring.EnrollmentSelect + " WHERE e.id=$1", enrollmentId));
        });
    }
}
public record ProfileInput(string FullName, string Role, string? Phone, int? Grade);
public record CourseInput(string SubjectId, string Title, int GradeMin, int GradeMax, string? ScheduleSummary);
public record LearnerInput(string Name, string Email, int Grade);
public record EnrollmentInput(Guid LearnerId, string[] SubjectIds);
public record EnrollmentUpdate(string Action, string[]? SubjectIds);
