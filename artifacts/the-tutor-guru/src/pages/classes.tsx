import { useState } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { Video } from 'lucide-react';
import { useListSessions, useListCourses, useCreateSession, useUpdateSession, getListSessionsQueryKey, getGetOverviewQueryKey, type ClassSession } from '@workspace/api-client-react';
import { PageHeader, ErrorState, ListSkeleton, Panel, Empty, Pill, F, selectCls } from '@/components/kit';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Textarea } from '@/components/ui/textarea';
import { Dialog, DialogContent, DialogHeader, DialogTitle, DialogTrigger } from '@/components/ui/dialog';
import { CourseDialog } from '@/pages/subjects';
import { useMe } from '@/hooks/use-me';
import { useToast } from '@/hooks/use-toast';
import { fmtDate, toLocalInput } from '@/lib/format';

function SessionForm({ session, trigger }: { session?: ClassSession; trigger: React.ReactNode }) {
  const courses = useListCourses();
  const create = useCreateSession();
  const update = useUpdateSession();
  const qc = useQueryClient();
  const { toast } = useToast();
  const [open, setOpen] = useState(false);
  const [f, setF] = useState({ courseId: session?.courseId ?? '', title: session?.title ?? '', startsAt: session ? toLocalInput(session.startsAt) : '', duration: String(session?.durationMinutes ?? 60), meetingUrl: session?.meetingUrl ?? '', lessonPlan: session?.lessonPlan ?? '', status: session?.status ?? 'scheduled' });
  const done = () => { qc.invalidateQueries({ queryKey: getListSessionsQueryKey() }); qc.invalidateQueries({ queryKey: getGetOverviewQueryKey() }); toast({ title: session ? 'Class updated' : 'Class scheduled' }); setOpen(false); };
  const fail = () => toast({ title: 'Could not save class', variant: 'destructive' });
  const submit = (e: React.FormEvent) => {
    e.preventDefault();
    const base = { title: f.title.trim(), startsAt: new Date(f.startsAt).toISOString(), durationMinutes: Number(f.duration), meetingUrl: f.meetingUrl.trim() || null, lessonPlan: f.lessonPlan.trim() || null };
    if (session) update.mutate({ sessionId: session.id, data: { ...base, status: f.status as ClassSession['status'] } }, { onSuccess: done, onError: fail });
    else create.mutate({ data: { ...base, courseId: f.courseId } }, { onSuccess: done, onError: fail });
  };
  return (
    <Dialog open={open} onOpenChange={setOpen}>
      <DialogTrigger asChild>{trigger}</DialogTrigger>
      <DialogContent className="max-h-[90dvh] overflow-auto"><DialogHeader><DialogTitle>{session ? 'Edit class' : 'Schedule a class'}</DialogTitle></DialogHeader>
        <form onSubmit={submit} className="space-y-4">
          {!session && <F label="Course"><select required className={selectCls} value={f.courseId} onChange={(e) => setF({ ...f, courseId: e.target.value })} data-testid="select-session-course"><option value="">Choose course</option>{courses.data?.map((c) => <option key={c.id} value={c.id}>{c.title}</option>)}</select></F>}
          <F label="Title"><Input required minLength={2} value={f.title} onChange={(e) => setF({ ...f, title: e.target.value })} data-testid="input-session-title" /></F>
          <div className="grid grid-cols-2 gap-3">
            <F label="Starts"><Input required type="datetime-local" value={f.startsAt} onChange={(e) => setF({ ...f, startsAt: e.target.value })} data-testid="input-session-start" /></F>
            <F label="Minutes"><Input type="number" value={60} readOnly data-testid="input-session-duration" /></F>
          </div>
          <F label="Teams or Zoom link (paste manually)"><Input type="url" placeholder="https://" value={f.meetingUrl} onChange={(e) => setF({ ...f, meetingUrl: e.target.value })} data-testid="input-session-url" /></F>
          <F label="Lesson plan"><Textarea rows={3} value={f.lessonPlan} onChange={(e) => setF({ ...f, lessonPlan: e.target.value })} data-testid="input-session-plan" /></F>
          {session && <F label="Status"><select className={selectCls} value={f.status} onChange={(e) => setF({ ...f, status: e.target.value as ClassSession['status'] })} data-testid="select-session-status">{['scheduled', 'live', 'completed', 'cancelled'].map((s) => <option key={s}>{s}</option>)}</select></F>}
          <Button type="submit" disabled={create.isPending || update.isPending} data-testid="button-save-session">{create.isPending || update.isPending ? 'Saving...' : 'Save class'}</Button>
        </form>
      </DialogContent>
    </Dialog>
  );
}
export default function Classes() {
  const { role } = useMe();
  const { data, isLoading, isError, refetch } = useListSessions();
  return (
    <>
      <PageHeader eyebrow="Timetable" title="Classes">
        {role === 'tutor' && <><CourseDialog trigger={<Button variant="outline" data-testid="button-new-course">New course</Button>} /><SessionForm trigger={<Button data-testid="button-new-session">Schedule class</Button>} /></>}
      </PageHeader>
      {isError ? <ErrorState onRetry={() => refetch()} /> : isLoading ? <ListSkeleton /> : !data?.length ? (
        <Empty title="No classes scheduled" body={role === 'tutor' ? 'Create a course, then schedule its two weekly live classes.' : 'Classes appear here once you have a paid, active subscription.'} />
      ) : (
        <div className="space-y-3">{data.map((s) => (
          <Panel key={s.id}><div className="flex flex-wrap items-start justify-between gap-3" data-testid={`row-session-${s.id}`}>
            <div className="min-w-0 flex-1">
              <p className="font-mono text-xs uppercase text-protea">{s.subjectName} / {s.courseTitle}</p>
              <h3 className="font-display text-lg font-bold">{s.title}</h3>
              <p className="text-sm">{fmtDate(s.startsAt)} / {s.durationMinutes} min / {s.tutorName}</p>
              {s.lessonPlan && <p className="mt-2 whitespace-pre-line rounded-md bg-muted p-2 text-sm">{s.lessonPlan}</p>}
              {role === 'tutor' && s.attendanceCount != null && <p className="mt-1 text-xs text-muted-foreground">{s.attendanceCount} attended</p>}
            </div>
            <div className="flex flex-col items-end gap-2">
              <Pill v={s.status} />
              {s.meetingUrl ? <Button size="sm" asChild><a href={s.meetingUrl} target="_blank" rel="noreferrer" data-testid={`link-join-${s.id}`}><Video className="mr-1 h-4 w-4" />Join</a></Button> : <span className="text-xs text-muted-foreground">Link not added yet</span>}
              {role === 'tutor' && <SessionForm session={s} trigger={<Button size="sm" variant="outline" data-testid={`button-edit-${s.id}`}>Edit</Button>} />}
            </div>
          </div></Panel>
        ))}</div>
      )}
    </>
  );
}
