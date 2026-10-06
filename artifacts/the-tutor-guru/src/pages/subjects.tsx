import { useState } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { useListSubjects, useListCourses, useCreateCourse, getListCoursesQueryKey } from '@workspace/api-client-react';
import { PageHeader, ErrorState, ListSkeleton, Panel, Empty, F, selectCls } from '@/components/kit';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Dialog, DialogContent, DialogHeader, DialogTitle, DialogTrigger } from '@/components/ui/dialog';
import { useMe } from '@/hooks/use-me';
import { useToast } from '@/hooks/use-toast';
import { rands } from '@/lib/format';

export function CourseDialog({ trigger }: { trigger: React.ReactNode }) {
  const { data: subjects } = useListSubjects();
  const qc = useQueryClient();
  const { toast } = useToast();
  const create = useCreateCourse();
  const [open, setOpen] = useState(false);
  const [f, setF] = useState({ subjectId: '', title: '', gradeMin: '8', gradeMax: '12', scheduleSummary: '' });
  const submit = (e: React.FormEvent) => {
    e.preventDefault();
    create.mutate({ data: { subjectId: f.subjectId, title: f.title.trim(), gradeMin: Number(f.gradeMin), gradeMax: Number(f.gradeMax), scheduleSummary: f.scheduleSummary || undefined } }, {
      onSuccess: () => { qc.invalidateQueries({ queryKey: getListCoursesQueryKey() }); toast({ title: 'Course created' }); setOpen(false); },
      onError: () => toast({ title: 'Could not create course', variant: 'destructive' }),
    });
  };
  return (
    <Dialog open={open} onOpenChange={setOpen}>
      <DialogTrigger asChild>{trigger}</DialogTrigger>
      <DialogContent>
        <DialogHeader><DialogTitle>Create a course</DialogTitle></DialogHeader>
        <form onSubmit={submit} className="space-y-4">
          <F label="Subject"><select required className={selectCls} value={f.subjectId} onChange={(e) => setF({ ...f, subjectId: e.target.value })} data-testid="select-course-subject"><option value="">Choose subject</option>{subjects?.map((s) => <option key={s.id} value={s.id}>{s.name}</option>)}</select></F>
          <F label="Title"><Input required minLength={2} value={f.title} onChange={(e) => setF({ ...f, title: e.target.value })} data-testid="input-course-title" /></F>
          <div className="grid grid-cols-2 gap-3">
            <F label="Lowest grade"><select className={selectCls} value={f.gradeMin} onChange={(e) => setF({ ...f, gradeMin: e.target.value })}>{[8, 9, 10, 11, 12].map((g) => <option key={g}>{g}</option>)}</select></F>
            <F label="Highest grade"><select className={selectCls} value={f.gradeMax} onChange={(e) => setF({ ...f, gradeMax: e.target.value })}>{[8, 9, 10, 11, 12].map((g) => <option key={g}>{g}</option>)}</select></F>
          </div>
          <F label="Schedule summary (optional)"><Input placeholder="Tue and Thu, 17:00" value={f.scheduleSummary} onChange={(e) => setF({ ...f, scheduleSummary: e.target.value })} data-testid="input-course-schedule" /></F>
          <Button type="submit" disabled={create.isPending || Number(f.gradeMin) > Number(f.gradeMax)} data-testid="button-save-course">{create.isPending ? 'Creating...' : 'Create course'}</Button>
        </form>
      </DialogContent>
    </Dialog>
  );
}
export default function Subjects() {
  const { role } = useMe();
  const subs = useListSubjects();
  const courses = useListCourses();
  return (
    <>
      <PageHeader eyebrow="Catalog" title="Subjects and courses">
        {role === 'tutor' && <CourseDialog trigger={<Button data-testid="button-new-course">New course</Button>} />}
      </PageHeader>
      <p className="mb-6 rounded-lg bg-accent p-3 text-sm">Courses are visible to everyone. Lessons and materials unlock only for a paid, active subscription, or for the tutor who owns the course.</p>
      <h2 className="mb-3 font-display text-xl font-bold">Subjects</h2>
      {subs.isError ? <ErrorState onRetry={() => subs.refetch()} /> : subs.isLoading ? <ListSkeleton rows={2} /> : (
        <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-3">{subs.data?.map((s) => (
          <Panel key={s.id}><div data-testid={`card-subject-${s.id}`}><h3 className="font-display font-bold">{s.name}</h3><p className="font-mono text-xs text-protea">Grades {s.grades.join(', ')} / {rands(s.priceMonthlyZar)}</p>{s.description && <p className="mt-1 text-sm text-muted-foreground">{s.description}</p>}</div></Panel>
        ))}</div>
      )}
      <h2 className="mb-3 mt-10 font-display text-xl font-bold">Courses</h2>
      {courses.isError ? <ErrorState onRetry={() => courses.refetch()} /> : courses.isLoading ? <ListSkeleton /> : !courses.data?.length ? (
        <Empty title="No courses yet" body={role === 'tutor' ? 'Create your first course to start scheduling classes.' : 'Tutors have not published courses yet.'} />
      ) : (
        <div className="grid gap-3 md:grid-cols-2">{courses.data.map((c) => (
          <Panel key={c.id}><div data-testid={`card-course-${c.id}`}><p className="font-mono text-xs uppercase text-protea">{c.subjectName} / Grades {c.gradeMin}-{c.gradeMax}</p><h3 className="font-display text-lg font-bold">{c.title}</h3><p className="text-sm text-muted-foreground">Tutor: {c.tutorName}</p>{c.scheduleSummary && <p className="mt-1 text-sm">{c.scheduleSummary}</p>}</div></Panel>
        ))}</div>
      )}
    </>
  );
}
