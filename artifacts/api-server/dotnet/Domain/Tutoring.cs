using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using TutorGuru.Api.Data;

namespace TutorGuru.Api;

public sealed class HttpError(int status, string message) : Exception(message)
{
    public int Status { get; } = status;
}
public sealed class Tutoring(Database db, IHostEnvironment environment, UserManager<IdentityUser> users)
{
    // Development test payments must never grant access on a published production service.
    public string PaymentEnvironmentFilter => environment.IsDevelopment() ? "" :
        " AND NOT EXISTS (SELECT 1 FROM guru_billing_accounts b WHERE b.enrollment_id=e.id AND b.mode='sandbox')";
    public const string ProfileSelect = """SELECT id,full_name AS "fullName",email,role,phone,grade,created_at AS "createdAt" FROM guru_profiles""";
    public const string CourseSelect = """SELECT c.id,c.subject_id AS "subjectId",s.name AS "subjectName",c.title,c.grade_min AS "gradeMin",c.grade_max AS "gradeMax",p.full_name AS "tutorName",c.schedule_summary AS "scheduleSummary" FROM guru_courses c JOIN guru_subjects s ON s.id=c.subject_id JOIN guru_profiles p ON p.id=c.tutor_id""";
    public const string EnrollmentSelect = """SELECT e.id,e.learner_id AS "learnerId",l.name AS "learnerName",l.grade AS "learnerGrade",e.subject_ids AS "subjectIds",e.monthly_amount_zar AS "monthlyAmountZar",e.status,e.payment_status AS "paymentStatus",e.period_ends_at AS "periodEndsAt",e.created_at AS "createdAt" FROM guru_enrollments e JOIN guru_learners l ON l.id=e.learner_id""";
    public const string SessionSelect = """SELECT ss.id,ss.course_id AS "courseId",c.title AS "courseTitle",s.name AS "subjectName",ss.title,ss.starts_at AS "startsAt",ss.duration_minutes AS "durationMinutes",ss.status,ss.meeting_url AS "meetingUrl",ss.lesson_plan AS "lessonPlan",p.full_name AS "tutorName",(SELECT count(*)::int FROM guru_attendance a WHERE a.session_id=ss.id AND a.attended) AS "attendanceCount" FROM guru_sessions ss JOIN guru_courses c ON c.id=ss.course_id JOIN guru_subjects s ON s.id=c.subject_id JOIN guru_profiles p ON p.id=c.tutor_id""";
    public static IDictionary<string, object?> Profile(HttpContext c) => (IDictionary<string, object?>)c.Items["profile"]!;
    public static string Id(HttpContext c) => (string)c.Items["profileId"]!;
    public static string Role(HttpContext c) => (string)Profile(c)["role"]!;
    public static void RequireRole(HttpContext c, string role)
    { if (Role(c) != role) throw new HttpError(403, $"This action is available to {role}s only."); }
    public async Task Load(HttpContext c, bool needsProfile)
    {
        var identityId = c.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (identityId is null) throw new HttpError(401, "Please sign in to continue.");
        var identity = await users.GetUserAsync(c.User);
        if (identity is null) throw new HttpError(401, "Please sign in to continue.");
        if (await users.IsLockedOutAsync(identity)) throw new HttpError(403, "This account's access is suspended.");
        var mapped = await db.One("SELECT profile_id FROM guru_identity_profile_links WHERE identity_id=$1", identityId);
        var id = mapped?["profile_id"]?.ToString() ?? identityId;
        c.Items["profileId"] = id;
        var p = await db.One(ProfileSelect + " WHERE id=$1", id);
        if (p is null && needsProfile) throw new HttpError(409, "Complete your account setup or link your existing account first.");
        if (p is not null) c.Items["profile"] = p;
    }
    public async Task<Guid[]> Accessible(HttpContext c)
    {
        if (Role(c) == "admin") return (await db.Query("SELECT id FROM guru_courses")).Select(r => (Guid)r["id"]!).ToArray();
        var p = Profile(c);
        if (Role(c) == "tutor")
            return (await db.Query("SELECT id FROM guru_courses WHERE tutor_id=$1", Id(c))).Select(r => (Guid)r["id"]!).ToArray();
        var rows = await db.Query("""
            SELECT DISTINCT c.id FROM guru_courses c
            JOIN guru_enrollments e ON c.subject_id=ANY(e.subject_ids)
            JOIN guru_learners l ON l.id=e.learner_id AND l.grade BETWEEN c.grade_min AND c.grade_max
            WHERE e.status IN ('active','cancelling') AND e.payment_status='paid' AND e.period_ends_at>now()
            """ + PaymentEnvironmentFilter + " AND " + (Role(c) == "parent" ? "e.parent_id=$1" : "lower(l.email)=lower($1)"), Role(c) == "parent" ? Id(c) : p["email"]);
        return rows.Select(r => (Guid)r["id"]!).ToArray();
    }
    public async Task<IDictionary<string, object?>> Owner(HttpContext c, Guid courseId)
    {
        if (Role(c) == "admin") return await db.One("SELECT * FROM guru_courses WHERE id=$1", courseId) ?? throw new HttpError(404, "Course not found.");
        RequireRole(c, "tutor");
        return await db.One("SELECT * FROM guru_courses WHERE id=$1 AND tutor_id=$2", courseId, Id(c)) ?? throw new HttpError(404, "Course not found.");
    }
    public async Task<List<IDictionary<string, object?>>> Learners(HttpContext c)
    {
        if (Role(c) == "admin") return await db.Query("""SELECT id,name,email,grade,created_at AS "createdAt" FROM guru_learners ORDER BY name""");
        if (Role(c) == "parent") return await db.Query("""SELECT id,name,email,grade,created_at AS "createdAt" FROM guru_learners WHERE parent_id=$1 ORDER BY created_at DESC""", Id(c));
        if (Role(c) == "learner") return await db.Query("""SELECT id,name,email,grade,created_at AS "createdAt" FROM guru_learners WHERE lower(email)=lower($1)""", Profile(c)["email"]);
        return await db.Query("""
            SELECT DISTINCT l.id,l.name,l.email,l.grade,l.created_at AS "createdAt"
            FROM guru_learners l JOIN guru_enrollments e ON e.learner_id=l.id
            JOIN guru_courses c ON c.subject_id=ANY(e.subject_ids) AND l.grade BETWEEN c.grade_min AND c.grade_max
            WHERE c.tutor_id=$1 AND e.payment_status='paid' AND e.status IN ('active','cancelling') AND e.period_ends_at>now()
            """ + PaymentEnvironmentFilter, Id(c));
    }
    public async Task ValidateSubjects(string[]? ids, int grade)
    {
        if (ids is null || ids.Length < 1 || ids.Length > 2 || ids.Distinct().Count() != ids.Length)
            throw new HttpError(400, "Choose one or two different subjects.");
        if ((await db.Query("SELECT id FROM guru_subjects WHERE id=ANY($1::text[]) AND $2=ANY(grades)", ids, grade)).Count != ids.Length)
            throw new HttpError(400, "One or more selected subjects are not available for this grade.");
    }
    public static void Text(string? value, string name, int min = 2, int max = 10000)
    { if (value is null || value.Trim().Length < min || value.Length > max) throw new HttpError(400, $"Enter a valid {name} ({min}–{max} characters)."); }
    public static void Grade(int value)
    { if (value < 8 || value > 12) throw new HttpError(400, "Choose a school grade from 8 to 12."); }
    public static void Meeting(string? value)
    {
        if (string.IsNullOrEmpty(value)) return;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var u) || u.Scheme != "https" ||
            !(u.Host == "teams.microsoft.com" || u.Host.EndsWith(".teams.microsoft.com") || u.Host == "teams.live.com" || u.Host == "zoom.us" || u.Host.EndsWith(".zoom.us")))
            throw new HttpError(400, "Use a secure Microsoft Teams or Zoom meeting link.");
    }
}
