import { useState } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { useListEnrollments, useCreateEnrollment, useUpdateEnrollment, useListLearners, useListSubjects, getListEnrollmentsQueryKey, getGetOverviewQueryKey, type Enrollment } from '@workspace/api-client-react';
import { PageHeader, ErrorState, ListSkeleton, Panel, Empty, Pill, F, selectCls } from '@/components/kit';
import { Button } from '@/components/ui/button';
import { Dialog, DialogContent, DialogHeader, DialogTitle, DialogTrigger } from '@/components/ui/dialog';
import { useToast } from '@/hooks/use-toast';
import { rands, priceFor, fmtDay } from '@/lib/format';

function Picker({ picked, setPicked, grade }: { picked: string[]; setPicked: (v: string[]) => void; grade?: number }) {
  const { data } = useListSubjects();
  const list = data?.filter((s) => !grade || s.grades.includes(grade));
  const toggle = (id: string) => setPicked(picked.includes(id) ? picked.filter((x) => x !== id) : picked.length < 2 ? [...picked, id] : picked);
  return (
    <div className="space-y-2">
      <div className="grid gap-2 sm:grid-cols-2">{list?.map((s) => (
        <button type="button" key={s.id} onClick={() => toggle(s.id)} data-testid={`button-pick-${s.id}`} className={`rounded-lg border p-3 text-left text-sm transition ${picked.includes(s.id) ? 'border-primary bg-primary text-primary-foreground' : 'hover:border-primary'}`}>{s.name}</button>
      ))}</div>
      <p className="text-sm">Monthly total: <b data-testid="text-total">{rands(priceFor(picked.length))}</b> (pick one or two)</p>
    </div>
  );
}
function Request() {
  const learners = useListLearners();
  const qc = useQueryClient();
  const { toast } = useToast();
  const create = useCreateEnrollment();
  const [open, setOpen] = useState(false);
  const [lid, setLid] = useState('');
  const [picked, setPicked] = useState<string[]>([]);
  const learner = learners.data?.find((l) => l.id === lid);
  const submit = (e: React.FormEvent) => {
    e.preventDefault();
    create.mutate({ data: { learnerId: lid, subjectIds: picked } }, {
      onSuccess: () => { qc.invalidateQueries({ queryKey: getListEnrollmentsQueryKey() }); qc.invalidateQueries({ queryKey: getGetOverviewQueryKey() }); toast({ title: 'Request recorded', description: 'It stays pending until payment is set up.' }); setOpen(false); setPicked([]); },
      onError: () => toast({ title: 'Could not create request', variant: 'destructive' }),
    });
  };
  return (
    <Dialog open={open} onOpenChange={setOpen}>
      <DialogTrigger asChild><Button data-testid="button-new-subscription">Request subscription</Button></DialogTrigger>
      <DialogContent><DialogHeader><DialogTitle>Request a subscription</DialogTitle></DialogHeader>
        <form onSubmit={submit} className="space-y-4">
          <F label="Learner"><select required className={selectCls} value={lid} onChange={(e) => { setLid(e.target.value); setPicked([]); }} data-testid="select-sub-learner"><option value="">Choose learner</option>{learners.data?.map((l) => <option key={l.id} value={l.id}>{l.name} (Grade {l.grade})</option>)}</select></F>
          {lid && <Picker picked={picked} setPicked={setPicked} grade={learner?.grade} />}
          <p className="rounded-md bg-accent p-2 text-xs">Payment is not connected yet. Your request will be saved as pending and no content unlocks until payment is live.</p>
          <Button type="submit" disabled={!lid || !picked.length || create.isPending} data-testid="button-submit-subscription">{create.isPending ? 'Sending...' : 'Send request'}</Button>
        </form>
      </DialogContent>
    </Dialog>
  );
}
function Row({ e }: { e: Enrollment }) {
  const qc = useQueryClient();
  const { toast } = useToast();
  const upd = useUpdateEnrollment();
  const subjects = useListSubjects();
  const [change, setChange] = useState(false);
  const [picked, setPicked] = useState<string[]>(e.subjectIds);
  const done = () => { qc.invalidateQueries({ queryKey: getListEnrollmentsQueryKey() }); qc.invalidateQueries({ queryKey: getGetOverviewQueryKey() }); };
  const run = (action: 'cancel' | 'resume' | 'change') => upd.mutate({ enrollmentId: e.id, data: action === 'change' ? { action, subjectIds: picked } : { action } }, {
    onSuccess: () => { done(); setChange(false); toast({ title: 'Subscription updated' }); },
    onError: () => toast({ title: 'Update failed', variant: 'destructive' }),
  });
  const names = e.subjectIds.map((id) => subjects.data?.find((s) => s.id === id)?.name ?? id).join(', ');
  return (
    <Panel><div data-testid={`row-enrollment-${e.id}`}>
      <div className="flex flex-wrap items-start justify-between gap-2">
        <div><h3 className="font-display text-lg font-bold">{e.learnerName} <span className="text-sm font-normal text-muted-foreground">Grade {e.learnerGrade}</span></h3><p className="text-sm">{names}</p></div>
        <div className="text-right"><p className="font-display text-2xl font-extrabold">{rands(e.monthlyAmountZar)}<span className="text-xs font-normal">/mo</span></p><div className="flex justify-end gap-1"><Pill v={e.status} /><Pill v={e.paymentStatus} /></div></div>
      </div>
      {e.paymentStatus === 'awaiting_provider' && <p className="mt-3 rounded-md bg-accent p-2 text-xs">Awaiting payment-gateway setup. Nothing has been charged and lessons are not unlocked.</p>}
      <p className="mt-2 font-mono text-xs text-muted-foreground">Requested {fmtDay(e.createdAt)}{e.periodEndsAt ? ` / period ends ${fmtDay(e.periodEndsAt)}` : ''}</p>
      <div className="mt-3 flex flex-wrap gap-2">
        {e.status === 'pending' && <Button size="sm" variant="outline" onClick={() => setChange(!change)} data-testid={`button-change-${e.id}`}>Change subjects</Button>}
        {e.status === 'cancelling' && <Button size="sm" onClick={() => run('resume')} disabled={upd.isPending} data-testid={`button-resume-${e.id}`}>Resume</Button>}
        {(e.status === 'pending' || e.status === 'active') && <Button size="sm" variant="ghost" className="text-destructive" onClick={() => run('cancel')} disabled={upd.isPending} data-testid={`button-cancel-${e.id}`}>Cancel</Button>}
      </div>
      {e.status === 'active' && <p className="mt-2 text-xs text-muted-foreground">Changing subjects on a paid subscription is unavailable until the payment gateway is connected.</p>}
      {change && (
        <div className="mt-3 space-y-3 border-t pt-3"><Picker picked={picked} setPicked={setPicked} grade={e.learnerGrade} />
          <Button size="sm" disabled={!picked.length || upd.isPending} onClick={() => run('change')} data-testid={`button-save-change-${e.id}`}>Save subjects</Button></div>
      )}
    </div></Panel>
  );
}
export default function Subscriptions() {
  const { data, isLoading, isError, refetch } = useListEnrollments();
  return (
    <>
      <PageHeader eyebrow="Billing" title="Subscriptions"><Request /></PageHeader>
      <div className="mb-6 rounded-xl border-l-4 border-secondary bg-card p-4 text-sm">R290/month for one subject, R495/month for two. Payments are not connected yet, so requests wait as pending until the payment gateway is set up. Full refund within 30 days once live.</div>
      {isError ? <ErrorState onRetry={() => refetch()} /> : isLoading ? <ListSkeleton /> : !data?.length ? <Empty title="No subscription requests" body="Add a learner, then request one or two subjects." /> : <div className="space-y-3">{data.map((e) => <Row key={e.id} e={e} />)}</div>}
    </>
  );
}
