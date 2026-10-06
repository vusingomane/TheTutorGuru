import { Router } from "express";
import {
  ListMaterialsQueryParams, ListMaterialsResponse, CreateMaterialBody, CreateMaterialResponse,
  DeleteMaterialParams, ListRecordingsResponse, CreateRecordingBody, CreateRecordingResponse,
  RequestUploadUrlBody, RequestUploadUrlResponse,
} from "@workspace/api-zod";
import { ObjectStorageService, ObjectNotFoundError } from "../lib/objectStorage";
import { query, requireUser, requireProfile, requireRole, accessibleCourses, assertCourseOwner, HttpError } from "../lib/tutoring";

const router = Router();
const storage = new ObjectStorageService();
const materialSelect = `SELECT m.id,m.course_id AS "courseId",c.title AS "courseTitle",m.title,m.original_file_name AS "originalFileName",m.object_path AS "objectPath",m.content_type AS "contentType",m.size_bytes AS "sizeBytes",m.uploaded_at AS "uploadedAt" FROM guru_materials m JOIN guru_courses c ON c.id=m.course_id`;
const recordingSelect = `SELECT r.id,r.session_id AS "sessionId",ss.title AS "sessionTitle",c.title AS "courseTitle",r.title,r.object_path AS "objectPath",r.duration_seconds AS "durationSeconds",r.uploaded_at AS "uploadedAt" FROM guru_recordings r JOIN guru_sessions ss ON ss.id=r.session_id JOIN guru_courses c ON c.id=ss.course_id`;
const allowedTypes = new Set(["application/pdf","application/msword","application/vnd.openxmlformats-officedocument.wordprocessingml.document","application/vnd.ms-powerpoint","application/vnd.openxmlformats-officedocument.presentationml.presentation","text/plain","image/png","image/jpeg","video/mp4"]);
router.use(requireUser, requireProfile);
router.post("/storage/uploads/request-url", async (req, res) => {
  requireRole(req, "tutor");
  const data = RequestUploadUrlBody.parse(req.body);
  if (!allowedTypes.has(data.contentType)) throw new HttpError(400, "Upload a PDF, document, presentation, image, text file, or MP4 recording.");
  const limit = data.contentType === "video/mp4" ? 1024*1024*1024 : 25*1024*1024;
  if (data.size>limit) throw new HttpError(400, data.contentType === "video/mp4" ? "Recordings must be smaller than 1 GB." : "Learning materials must be smaller than 25 MB.");
  const uploadURL = await storage.getObjectEntityUploadURL();
  const objectPath = storage.normalizeObjectEntityPath(uploadURL);
  await query("INSERT INTO guru_uploads(object_path,owner_id,name,size,content_type) VALUES($1,$2,$3,$4,$5)", [objectPath, req.guruUserId, data.name.slice(0,255), data.size, data.contentType]);
  res.json(RequestUploadUrlResponse.parse({ uploadURL, objectPath }));
});
async function validateUpload(objectPath: string, ownerId: string) {
  if (!/^\/objects\/uploads\/[a-f0-9-]{36}$/.test(objectPath)) throw new HttpError(400, "Invalid uploaded file path.");
  const [upload] = await query("SELECT * FROM guru_uploads WHERE object_path=$1 AND owner_id=$2", [objectPath,ownerId]);
  if (!upload) throw new HttpError(403, "Upload this file from your own account.");
  const file = await storage.getObjectEntityFile(objectPath);
  const [metadata] = await file.getMetadata();
  if (Number(metadata.size) !== upload.size || metadata.contentType !== upload.content_type) throw new HttpError(400, "The uploaded file does not match its declared size or type.");
  await storage.trySetObjectEntityAclPolicy(objectPath, { owner:ownerId,visibility:"private" });
  return upload;
}
router.get("/materials", async (req,res) => {
  const params = ListMaterialsQueryParams.parse(req.query);
  const ids = await accessibleCourses(req);
  res.json(ListMaterialsResponse.parse(await query(`${materialSelect} WHERE m.course_id=ANY($1::uuid[]) AND ($2::uuid IS NULL OR m.course_id=$2) ORDER BY m.uploaded_at DESC`, [ids,params.courseId ?? null])));
});
router.post("/materials", async (req,res) => {
  const data = CreateMaterialBody.parse(req.body);
  await assertCourseOwner(req,data.courseId);
  const upload = await validateUpload(data.objectPath, req.guruUserId!);
  if (upload.content_type === "video/mp4") throw new HttpError(400, "Add videos from the Recordings page.");
  const [m] = await query(`INSERT INTO guru_materials(course_id,owner_id,title,original_file_name,object_path,content_type,size_bytes) VALUES($1,$2,$3,$4,$5,$6,$7) RETURNING id`, [data.courseId,req.guruUserId,data.title.trim(),upload.name,data.objectPath,upload.content_type,upload.size]);
  const [row] = await query(`${materialSelect} WHERE m.id=$1`, [m.id]);
  res.status(201).json(CreateMaterialResponse.parse(row));
});
router.delete("/materials/:materialId", async (req,res) => {
  const { materialId } = DeleteMaterialParams.parse(req.params);
  const [material] = await query("SELECT course_id FROM guru_materials WHERE id=$1", [materialId]);
  if (!material) throw new HttpError(404,"Material not found.");
  await assertCourseOwner(req,material.course_id);
  await query("DELETE FROM guru_materials WHERE id=$1", [materialId]);
  res.status(204).end();
});
router.get("/recordings", async (req,res) => {
  const ids = await accessibleCourses(req);
  res.json(ListRecordingsResponse.parse(await query(`${recordingSelect} WHERE ss.course_id=ANY($1::uuid[]) ORDER BY r.uploaded_at DESC`, [ids])));
});
router.post("/recordings", async (req,res) => {
  const data = CreateRecordingBody.parse(req.body);
  const [session] = await query("SELECT course_id FROM guru_sessions WHERE id=$1", [data.sessionId]);
  if (!session) throw new HttpError(404,"Session not found.");
  await assertCourseOwner(req,session.course_id);
  const upload = await validateUpload(data.objectPath,req.guruUserId!);
  if (upload.content_type !== "video/mp4") throw new HttpError(400,"Class recordings must be MP4 videos.");
  const [recording] = await query(`INSERT INTO guru_recordings(session_id,owner_id,title,object_path,duration_seconds) VALUES($1,$2,$3,$4,$5) RETURNING id`, [data.sessionId,req.guruUserId,data.title.trim(),data.objectPath,data.durationSeconds]);
  const [row] = await query(`${recordingSelect} WHERE r.id=$1`, [recording.id]);
  res.status(201).json(CreateRecordingResponse.parse(row));
});
router.get("/storage/objects/*objectPath", async (req,res) => {
  const raw = req.params.objectPath;
  const objectPath = `/objects/${Array.isArray(raw) ? raw.join("/") : raw}`;
  if (!/^\/objects\/uploads\/[a-f0-9-]{36}$/.test(objectPath)) throw new HttpError(404,"File not found.");
  const ids = await accessibleCourses(req);
  const [upload] = await query("SELECT * FROM guru_uploads WHERE object_path=$1", [objectPath]);
  if (!upload) throw new HttpError(404,"File not found.");
  const [visible] = await query(`SELECT 1 FROM guru_materials WHERE object_path=$1 AND course_id=ANY($2::uuid[]) UNION SELECT 1 FROM guru_recordings r JOIN guru_sessions s ON s.id=r.session_id WHERE r.object_path=$1 AND s.course_id=ANY($2::uuid[]) LIMIT 1`, [objectPath,ids]);
  if (upload.owner_id !== req.guruUserId && !visible) throw new HttpError(403,"This file is available to enrolled learners only.");
  try {
    const file = await storage.getObjectEntityFile(objectPath);
    const [metadata] = await file.getMetadata();
    const size = Number(metadata.size);
    res.set({ "Content-Type":upload.content_type, "Accept-Ranges":"bytes","Cache-Control":"private, no-store","X-Content-Type-Options":"nosniff" });
    if (upload.content_type !== "video/mp4") res.set("Content-Disposition",`attachment; filename*=UTF-8''${encodeURIComponent(upload.name)}`);
    let start = 0, end = size-1;
    const range = req.headers.range;
    if (range) {
      const match = /^bytes=(\d*)-(\d*)$/.exec(range);
      if (!match || (!match[1] && !match[2])) { res.status(416).set("Content-Range",`bytes */${size}`).end(); return; }
      if (!match[1]) start = Math.max(0,size-Number(match[2]));
      else { start=Number(match[1]); if(match[2]) end=Math.min(end,Number(match[2])); }
      if(start>=size || start>end) { res.status(416).set("Content-Range",`bytes */${size}`).end(); return; }
      res.status(206).set("Content-Range",`bytes ${start}-${end}/${size}`);
    }
    res.set("Content-Length",String(end-start+1));
    const stream = file.createReadStream({start,end});
    stream.on("error", err => { req.log.error({err},"File streaming failed"); if(!res.headersSent) res.status(500).end(); else res.destroy(err); });
    res.on("close",()=>stream.destroy());
    stream.pipe(res);
  } catch (error) {
    if(error instanceof ObjectNotFoundError) throw new HttpError(404,"Uploaded file not found.");
    throw error;
  }
});
export default router;
