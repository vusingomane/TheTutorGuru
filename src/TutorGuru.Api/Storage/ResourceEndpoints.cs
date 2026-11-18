using TutorGuru.Api.Data;

namespace TutorGuru.Api.Storage;

public static class ResourceEndpoints
{
    private const string MaterialSelect = """SELECT m.id,m.course_id AS "courseId",c.title AS "courseTitle",m.title,m.original_file_name AS "originalFileName",m.object_path AS "objectPath",m.content_type AS "contentType",m.size_bytes AS "sizeBytes",m.uploaded_at AS "uploadedAt" FROM guru_materials m JOIN guru_courses c ON c.id=m.course_id""";
    private const string RecordingSelect = """SELECT r.id,r.session_id AS "sessionId",ss.title AS "sessionTitle",c.title AS "courseTitle",r.title,r.object_path AS "objectPath",r.duration_seconds AS "durationSeconds",r.uploaded_at AS "uploadedAt" FROM guru_recordings r JOIN guru_sessions ss ON ss.id=r.session_id JOIN guru_courses c ON c.id=ss.course_id""";
    private static readonly HashSet<string> Types = ["application/pdf", "application/msword", "application/vnd.openxmlformats-officedocument.wordprocessingml.document", "application/vnd.ms-powerpoint", "application/vnd.openxmlformats-officedocument.presentationml.presentation", "text/plain", "image/png", "image/jpeg", "video/mp4"];
    private static async Task<IDictionary<string, object?>> ValidateUpload(string path, HttpContext c, Database db, ObjectStorage storage)
    {
        var row = await db.One("SELECT * FROM guru_uploads WHERE object_path=$1 AND owner_id=$2", path, Tutoring.Id(c)) ?? throw new HttpError(403, "Upload this file from your own account.");
        var (size, type) = await storage.Metadata(path);
        if (size != Convert.ToInt64(row["size"]) || type != row["content_type"]?.ToString()) throw new HttpError(400, "The uploaded file does not match its declared size or type.");
        return row;
    }
    public static void MapResources(this WebApplication app)
    {
        app.MapPost("/api/storage/uploads/request-url", async (UploadInput body, HttpContext c, Database db, ObjectStorage storage) =>
        {
            Tutoring.RequireRole(c, "tutor"); Tutoring.Text(body.Name, "file name", 1, 255);
            if (!Types.Contains(body.ContentType)) throw new HttpError(400, "Upload a PDF, document, presentation, image, text file, or MP4 recording.");
            var max = body.ContentType == "video/mp4" ? 1024L * 1024 * 1024 : 25L * 1024 * 1024;
            if (body.Size < 1 || body.Size > max) throw new HttpError(400, body.ContentType == "video/mp4" ? "Recordings must be smaller than 1 GB." : "Learning materials must be smaller than 25 MB.");
            var path = "/objects/uploads/" + Guid.NewGuid();
            var url = await storage.Sign(path, "PUT");
            await db.Query("INSERT INTO guru_uploads(object_path,owner_id,name,size,content_type) VALUES($1,$2,$3,$4,$5)", path, Tutoring.Id(c), body.Name, (int)body.Size, body.ContentType);
            return Results.Json(new { uploadURL = url, objectPath = path });
        });
        app.MapGet("/api/materials", async (Guid? courseId, HttpContext c, Database db, Tutoring domain) =>
            Results.Json(await db.Query(MaterialSelect + " WHERE m.course_id=ANY($1::uuid[]) AND ($2::uuid IS NULL OR m.course_id=$2) ORDER BY m.uploaded_at DESC", await domain.Accessible(c), courseId)));
        app.MapPost("/api/materials", async (MaterialInput body, HttpContext c, Database db, Tutoring domain, ObjectStorage storage) =>
        {
            Tutoring.Text(body.Title, "material title");
            await domain.Owner(c, body.CourseId);
            var upload = await ValidateUpload(body.ObjectPath, c, db, storage);
            if (upload["content_type"]?.ToString() == "video/mp4") throw new HttpError(400, "Add videos from the Recordings page.");
            var row = await db.One("INSERT INTO guru_materials(course_id,owner_id,title,original_file_name,object_path,content_type,size_bytes) VALUES($1,$2,$3,$4,$5,$6,$7) RETURNING id", body.CourseId, Tutoring.Id(c), body.Title.Trim(), upload["name"], body.ObjectPath, upload["content_type"], upload["size"]);
            return Results.Json(await db.One(MaterialSelect + " WHERE m.id=$1", row!["id"]), statusCode: 201);
        });
        app.MapDelete("/api/materials/{materialId:guid}", async (Guid materialId, HttpContext c, Database db, Tutoring domain) =>
        {
            var m = await db.One("SELECT course_id FROM guru_materials WHERE id=$1", materialId) ?? throw new HttpError(404, "Material not found.");
            await domain.Owner(c, (Guid)m["course_id"]!);
            await db.Query("DELETE FROM guru_materials WHERE id=$1", materialId);
            return Results.NoContent();
        });
        app.MapGet("/api/recordings", async (HttpContext c, Database db, Tutoring domain) =>
            Results.Json(await db.Query(RecordingSelect + " WHERE ss.course_id=ANY($1::uuid[]) ORDER BY r.uploaded_at DESC", (object)await domain.Accessible(c))));
        app.MapPost("/api/recordings", async (RecordingInput body, HttpContext c, Database db, Tutoring domain, ObjectStorage storage) =>
        {
            Tutoring.Text(body.Title, "recording title");
            if (body.DurationSeconds < 1) throw new HttpError(400, "Enter the recording length.");
            var s = await db.One("SELECT course_id FROM guru_sessions WHERE id=$1", body.SessionId) ?? throw new HttpError(404, "Session not found.");
            await domain.Owner(c, (Guid)s["course_id"]!);
            var upload = await ValidateUpload(body.ObjectPath, c, db, storage);
            if (upload["content_type"]?.ToString() != "video/mp4") throw new HttpError(400, "Class recordings must be MP4 videos.");
            var row = await db.One("INSERT INTO guru_recordings(session_id,owner_id,title,object_path,duration_seconds) VALUES($1,$2,$3,$4,$5) RETURNING id", body.SessionId, Tutoring.Id(c), body.Title.Trim(), body.ObjectPath, body.DurationSeconds);
            return Results.Json(await db.One(RecordingSelect + " WHERE r.id=$1", row!["id"]), statusCode: 201);
        });
        app.MapGet("/api/storage/objects/{**objectPath}", async (string objectPath, HttpContext c, Database db, Tutoring domain, ObjectStorage storage) =>
        {
            var path = "/objects/" + objectPath;
            var upload = await db.One("SELECT * FROM guru_uploads WHERE object_path=$1", path) ?? throw new HttpError(404, "File not found.");
            var visible = await db.One("SELECT 1 FROM guru_materials WHERE object_path=$1 AND course_id=ANY($2::uuid[]) UNION SELECT 1 FROM guru_recordings r JOIN guru_sessions s ON s.id=r.session_id WHERE r.object_path=$1 AND s.course_id=ANY($2::uuid[]) LIMIT 1", path, await domain.Accessible(c));
            if (upload["owner_id"]?.ToString() != Tutoring.Id(c) && visible is null) throw new HttpError(403, "This file is available to enrolled learners only.");
            await storage.Download(c, path, upload["content_type"]!.ToString()!, upload["name"]!.ToString()!);
        });
    }
}
public record UploadInput(string Name, long Size, string ContentType);
public record MaterialInput(Guid CourseId, string Title, string ObjectPath, string? OriginalFileName, string? ContentType, int SizeBytes);
public record RecordingInput(Guid SessionId, string Title, string ObjectPath, int DurationSeconds);
