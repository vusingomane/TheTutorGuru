import { useState } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { useListLearners, useCreateLearner, useListSessions, useMarkAttendance, getListLearnersQueryKey, getGetOverviewQueryKey, getListSessionsQueryKey, getListProgressQueryKey } from '@workspace/api-client-react';
import { PageHeader, ErrorState, ListSkeleton, Panel, Empty, F, selectCls } from '@/components/kit';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Dialog, DialogContent, DialogHeader, DialogTitle, DialogTrigger } from '@/components/ui/dialog';
import { useMe } from '@/hooks/use-me';
import { useToast } from '@/hooks/use-toast';
import { fmtDate } from '@/lib/format';

function AddLearner() {
  const qc = useQueryClient();
  const { toast } = useToast();
  const create = useCreateLearner();
  const [open, setOpen] = useState(false);
  const [f, setF] = useState({ name: '', email: '', grade: '10' });
  const submit = (e: React.FormEvent) => {
    e.preventDefault();
    create.mutate({ data: { name: f.name.trim(), email: f.email.trim(), grade: Number(f.grade) } }, {
      onSuccess: () => { qc.invalidateQueries({ queryKey: getListLearnersQueryKey() }); qc.invalidateQueries({ queryKey: getGetOverviewQueryKey() }); toast({ title: 'Learner added' }); setOpen(false); setF({ name: '', email: '', grade: '10' }); },
      onError: () => toast({ title: 'Could not add learner', variant: 'destructive' }),
    });
  };
  return (
    <Dialog open={open} onOpenChange={setOpen}>
      <DialogTrigger asChild><Button data-testid="button-add-learner">Add learner</Button></DialogTrigger>
      <DialogContent><DialogHeader><DialogTitle>Add a learner</DialogTitle></DialogHeader>
        <form onSubmit={submit} className="space-y-4">
          <F label="Name"><Input required minLength={2} value={f.name} onChange={(e) => setF({ ...f, name: e.target.value })} data-testid="input-learner-name" /></F>
          <F label="Email"><Input required type="email" value={f.email} onChange={(e) => setF({ ...f, email: e.target.value })} data-testid="input-learner-email" /></F>
          <F label="Grade"><select className={selectCls} value={f.grade} onChange={(e) => setF({ ...f, grade: e.target.value })} data-testid="select-learner-grade">{[8, 9, 10, 11, 12].map((g) => <option key={g}>{g}</option>)}</select></F>
          <Button type="submit" disabled={create.isPending} data-testid="button-save-learner">{create.isPending ? 'Adding...' : 'Add learner'}</Button>
        </form>
      </DialogContent>
    </Dialog>
  );
}
function Attendance({ learnerId }: { learnerId: string }) {
  const sessions = useListSessions();
  const qc = useQueryClient();
  const { toast } = useToast();
  const mark = useMarkAttendance();
  const [sid, setSid] = useState('');
  const run = (attended: boolean) => mark.mutate({ sessionId: sid, learnerId, data: { attended } }, {
    onSuccess: () => { qc.invalidateQueries({ queryKey: getListSessionsQueryKey() }); qc.invalidateQueries({ queryKey: getListProgressQueryKey() }); qc.invalidateQueries({ queryKey: getGetOverviewQueryKey() }); toast({ title: attended ? 'Marked present' : 'Marked absent' }); },
    onError: () => toast({ title: 'Could not mark attendance', variant: 'destructive' }),
  });
  return (
    <div className="mt-3 flex flex-wrap items-center gap-2">
      <select className={`${selectCls} max-w-xs`} value={sid} onChange={(e) => setSid(e.target.value)} data-testid={`select-session-${learnerId}`}>
        <option value="">Choose class</option>
        {sessions.data?.map((s) => <option key={s.id} value={s.id}>{s.title} - {fmtDate(s.startsAt)}</option>)}
      </select>
      <Button size="sm" disabled={!sid || mark.isPending} onClick={() => run(true)} data-testid={`button-present-${learnerId}`}>Present</Button>
      <Button size="sm" variant="outline" disabled={!sid || mark.isPending} onClick={() => run(false)} data-testid={`button-absent-${learnerId}`}>Absent</Button>
    </div>
  );
}
export default function Learners() {
  const { role } = useMe();
  const { data, isLoading, isError, refetch } = useListLearners();
  return (
    <>
      <PageHeader eyebrow={role === 'tutor' ? 'Roster' : 'Family'} title="Learners">{role === 'parent' && <AddLearner />}</PageHeader>
      {isError ? <ErrorState onRetry={() => refetch()} /> : isLoading ? <ListSkeleton /> : !data?.length ? (
        <Empty title="No learners yet" body={role === 'parent' ? 'Add your child to request a subscription.' : 'Learners appear here once they join your courses.'} action={role === 'parent' ? <AddLearner /> : undefined} />
      ) : (
        <div className="space-y-3">{data.map((l) => (
          <Panel key={l.id}><div data-testid={`row-learner-${l.id}`}>
            <div className="flex items-center gap-3"><div className="flex h-10 w-10 items-center justify-center rounded-full bg-secondary font-display font-bold">{l.name.slice(0, 1)}</div><div><p className="font-display font-bold">{l.name}</p><p className="text-sm text-muted-foreground">Grade {l.grade} / {l.email}</p></div></div>
            {role === 'tutor' && <Attendance learnerId={l.id} />}
          </div></Panel>
        ))}</div>
      )}
    </>
  );
}
