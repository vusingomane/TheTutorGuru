using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using TutorGuru.Api.Billing;
using TutorGuru.Api.Data;

namespace TutorGuru.Api;

public static class AdminEndpoints
{
    public static void MapAdmin(this WebApplication app)
    {
        var routes = app.MapGroup("/api/admin").RequireAuthorization();
        routes.AddEndpointFilter(async (invocation, next) =>
        {
            var c = invocation.HttpContext;
            if (Tutoring.Role(c) != "admin") throw new HttpError(403, "Administrator access is required.");
            var users = c.RequestServices.GetRequiredService<UserManager<IdentityUser>>();
            var user = await users.GetUserAsync(c.User);
            if (user is null || !await new TutorGuru.Api.Identity.AccountActivation(
                    c.RequestServices.GetRequiredService<Database>()).IsConfirmedAsync(users, user) || await users.IsLockedOutAsync(user))
                throw new HttpError(403, "An active administrator account is required.");
            return await next(invocation);
        });
        routes.MapGet("/overview", async (Database db) => Results.Json(await db.One("""
            SELECT (SELECT count(*)::int FROM guru_identity_users) AS accounts,
            (SELECT count(*)::int FROM guru_courses) AS courses,
            (SELECT count(*)::int FROM guru_enrollments) AS subscriptions,
            (SELECT count(*)::int FROM guru_enrollments WHERE status IN ('active','cancelling') AND payment_status='paid' AND period_ends_at>now()) AS "activeSubscriptions"
            """)));
        routes.MapGet("/users", async (Database db) => Results.Json(await db.Query("""
            SELECT u.id,u.data->>'Email' AS email,coalesce(p.full_name,'Profile setup pending') AS "fullName",
            coalesce(p.role,'unassigned') AS role,coalesce((u.data->>'EmailConfirmed')::boolean,false) AS "emailConfirmed",
            coalesce((u.data->>'PlatformActivated')::boolean,(u.data->>'EmailConfirmed')::boolean,false) AS activated,
            (coalesce((u.data->>'LockoutEnabled')::boolean,false) AND coalesce((u.data->>'LockoutEnd')::timestamptz,'-infinity')>now()) AS "lockedOut"
            FROM guru_identity_users u LEFT JOIN guru_identity_profile_links l ON l.identity_id=u.id
            LEFT JOIN guru_profiles p ON p.id=coalesce(l.profile_id,u.id) ORDER BY lower(u.data->>'Email')
            """)));
        routes.MapPatch("/users/{identityId}/activation", async (string identityId, AdminActivationInput body, Database db, HttpContext c, UserManager<IdentityUser> users, ILoggerFactory logs) =>
        {
            if (body.Activated is not true) throw new HttpError(400, "Use account suspension to disable an active account.");
            var user = await users.FindByIdAsync(identityId) ?? throw new HttpError(404, "Account not found.");
            // Preserve password, email confirmation, role, and suspension state.
            await db.Query("""
                UPDATE guru_identity_users SET data=data || jsonb_build_object('PlatformActivated',true,'ConcurrencyStamp',$2::text),
                    concurrency_stamp=$2 WHERE id=$1
                """, identityId, Guid.NewGuid().ToString());
            logs.CreateLogger("AdministratorAudit").LogInformation("Administrator {Actor} activated account {Target}",
                c.User.FindFirstValue(ClaimTypes.NameIdentifier), identityId);
            return Results.Json(new { message = "Account activated. The user can now sign in unless access is suspended." });
        });
        routes.MapPatch("/users/{identityId}/access", async (string identityId, AdminAccessInput body, HttpContext c, Database db, UserManager<IdentityUser> users, ILoggerFactory logs) =>
        {
            var locked = body.Locked ?? throw new HttpError(400, "Choose whether to suspend or restore access.");
            if (identityId == c.User.FindFirstValue(ClaimTypes.NameIdentifier)) throw new HttpError(400, "You cannot change your own account access.");
            var user = await users.FindByIdAsync(identityId) ?? throw new HttpError(404, "Account not found.");
            var admin = await db.One("""
                SELECT p.role FROM guru_identity_users u LEFT JOIN guru_identity_profile_links l ON l.identity_id=u.id
                JOIN guru_profiles p ON p.id=coalesce(l.profile_id,u.id) WHERE u.id=$1
                """, identityId);
            if (admin?["role"]?.ToString() == "admin") throw new HttpError(400, "Administrator access cannot be disabled here.");
            Check(await users.SetLockoutEnabledAsync(user, true));
            Check(await users.SetLockoutEndDateAsync(user, locked ? DateTimeOffset.MaxValue : null));
            if (!locked) Check(await users.ResetAccessFailedCountAsync(user));
            Check(await users.UpdateSecurityStampAsync(user));
            logs.CreateLogger("AdministratorAudit").LogInformation("Administrator {Actor} set account {Target} locked={Locked}", c.User.FindFirstValue(ClaimTypes.NameIdentifier), identityId, locked);
            return Results.Json(new { message = locked ? "Account access suspended." : "Account access restored." });
        });
        routes.MapGet("/tutors", async (Database db) => Results.Json(await db.Query("""SELECT id,full_name AS "fullName",email FROM guru_profiles WHERE role='tutor' ORDER BY full_name""")));
        routes.MapGet("/courses", async (Database db) => Results.Json(await db.Query(Tutoring.CourseSelect.Replace("SELECT c.id,", """SELECT c.tutor_id AS "tutorId",c.id,""") + " ORDER BY c.title")));
        routes.MapPatch("/courses/{courseId:guid}", async (Guid courseId, AdminCourseInput body, Database db, HttpContext c, ILoggerFactory logs) =>
        {
            Tutoring.Text(body.Title, "course title", 2, 200);
            Tutoring.Grade(body.GradeMin); Tutoring.Grade(body.GradeMax);
            if (body.GradeMin > body.GradeMax) throw new HttpError(400, "Minimum grade cannot exceed maximum grade.");
            if (body.ScheduleSummary?.Length > 1000) throw new HttpError(400, "The schedule summary is too long.");
            var course = await db.One("SELECT subject_id FROM guru_courses WHERE id=$1", courseId) ?? throw new HttpError(404, "Course not found.");
            if (await db.One("SELECT id FROM guru_profiles WHERE id=$1 AND role='tutor'", body.TutorId) is null) throw new HttpError(400, "Choose an existing tutor.");
            var subject = await db.One("SELECT grades FROM guru_subjects WHERE id=$1", course["subject_id"]);
            var grades = (int[])subject!["grades"]!;
            if (!grades.Contains(body.GradeMin) || !grades.Contains(body.GradeMax)) throw new HttpError(400, "These grades are not available for this subject.");
            await db.Query("UPDATE guru_courses SET title=$1,tutor_id=$2,grade_min=$3,grade_max=$4,schedule_summary=$5 WHERE id=$6",
                body.Title.Trim(), body.TutorId, body.GradeMin, body.GradeMax, body.ScheduleSummary, courseId);
            logs.CreateLogger("AdministratorAudit").LogInformation("Administrator {Actor} updated course {Course}", c.User.FindFirstValue(ClaimTypes.NameIdentifier), courseId);
            return Results.Json(await db.One(Tutoring.CourseSelect + " WHERE c.id=$1", courseId));
        });
        routes.MapGet("/subscriptions", async (Database db) => Results.Json(await db.Query(
            Tutoring.EnrollmentSelect.Replace("SELECT e.id,", """SELECT p.full_name AS "parentName",p.email AS "parentEmail",e.id,""") +
            " JOIN guru_profiles p ON p.id=e.parent_id ORDER BY e.created_at DESC")));
        routes.MapPost("/subscriptions/{enrollmentId:guid}/cancel", async (Guid enrollmentId, Database db, BillingService billing, Tutoring domain, HttpContext c, ILoggerFactory logs) =>
        {
            var readiness = await db.One("SELECT to_regclass('public.guru_billing_accounts') IS NOT NULL AS ready");
            if (readiness?["ready"] is not true) throw new HttpError(503, "Billing setup is incomplete. No subscription or payment changes were made.");
            var enrollment = await db.One("SELECT parent_id FROM guru_enrollments WHERE id=$1", enrollmentId) ?? throw new HttpError(404, "Subscription not found.");
            await billing.Change(enrollmentId, (string)enrollment["parent_id"]!, new EnrollmentUpdate("cancel", null), domain);
            logs.CreateLogger("AdministratorAudit").LogInformation("Administrator {Actor} cancelled subscription {Enrollment}", c.User.FindFirstValue(ClaimTypes.NameIdentifier), enrollmentId);
            return Results.Json(new { message = "Cancellation recorded. Any current paid access remains until the billing period ends." });
        });
    }
    private static void Check(IdentityResult result)
    { if (!result.Succeeded) throw new HttpError(409, "The account changed while saving. Refresh and try again."); }
}
public record AdminAccessInput(bool? Locked);
public record AdminActivationInput(bool? Activated);
public record AdminCourseInput(string Title, string TutorId, int GradeMin, int GradeMax, string? ScheduleSummary);
