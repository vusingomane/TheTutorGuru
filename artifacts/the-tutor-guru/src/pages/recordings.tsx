import { useRef, useState } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { useListRecordings, useCreateRecording, useListSessions, getListRecordingsQueryKey, getGetOverviewQueryKey } from '@workspace/api-client-react';
import { PageHeader, ErrorState, ListSkeleton, Panel, Empty, F, selectCls } from '@/components/kit';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Dialog, DialogContent, DialogHeader, DialogTitle, DialogTrigger } from '@/components/ui/dialog';
import { useMe } from '@/hooks/use-me';
import { useToast } from '@/hooks/use-toast';
import { useFileUpload, storageUrl } from '@/lib/upload';
import { fmtDur, fmtDay } from '@/lib/format';

function Upload() {
  const sessions = useListSessions();
  const upload = useFileUpload();
  const create = useCreateRecording();
  const qc = useQueryClient();
  const { toast } = useToast();
  const [open, setOpen] = useState(false);
  const [busy, setBusy] = useState(false);
  const [sessionId, setSid] = useState('');
  const [title, setTitle] = useState('');
  const [mins, setMins] = useState('60');
  const file = useRef<HTMLInputElement>(null);
  const submit = async (e: React.FormEvent) => {
    e.preventDefault();
    const f = file.current?.files?.[0];
    if (!f) return;
    setBusy(true);
    try {
      const { objectPath } = await upload(f);
      create.mutate({ data: { sessionId, title: title.trim(), objectPath, durationSeconds: Math.max(1, Math.round(Number(mins) * 60)) } }, {
        onSuccess: () => { qc.invalidateQueries({ queryKey: getListRecordingsQueryKey() }); qc.invalidateQueries({ queryKey: getGetOverviewQueryKey() }); toast({ title: 'Recording uploaded' }); setOpen(false); setTitle(''); },
        onError: () => toast({ title: 'Could not register recording', variant: 'destructive' }),
      });
    } catch (err) { toast({ title: err instanceof Error ? err.message : 'Upload failed', variant: 'destructive' }); }
    setBusy(false);
  };
  return (
    <Dialog open={open} onOpenChange={setOpen}>
      <DialogTrigger asChild><Button data-testid="button-upload-recording">Upload recording</Button></DialogTrigger>
      <DialogContent><DialogHeader><DialogTitle>Upload recording</DialogTitle></DialogHeader>
        <form onSubmit={submit} className="space-y-4">
          <F label="Class"><select required className={selectCls} value={sessionId} onChange={(e) => setSid(e.target.value)} data-testid="select-recording-session"><option value="">Choose class</option>{sessions.data?.map((s) => <option key={s.id} value={s.id}>{s.title} - {s.courseTitle}</option>)}</select></F>
          <F label="Title"><Input required minLength={2} value={title} onChange={(e) => setTitle(e.target.value)} data-testid="input-recording-title" /></F>
          <F label="Length (minutes)"><Input required type="number" min={1} value={mins} onChange={(e) => setMins(e.target.value)} data-testid="input-recording-minutes" /></F>
          <F label="Video file"><Input required type="file" accept="video/*,audio/*" ref={file} data-testid="input-recording-file" /></F>
          <Button type="submit" disabled={busy || create.isPending} data-testid="button-save-recording">{busy ? 'Uploading...' : 'Upload'}</Button>
        </form>
      </DialogContent>
    </Dialog>
  );
}
export default function Recordings() {
  const { role } = useMe();
  const { data, isLoading, isError, refetch } = useListRecordings();
  const [playing, setPlaying] = useState('');
  return (
    <>
      <PageHeader eyebrow="Catch up" title="Recordings">{role === 'tutor' && <Upload />}</PageHeader>
      {isError ? <ErrorState onRetry={() => refetch()} /> : isLoading ? <ListSkeleton /> : !data?.length ? (
        <Empty title="No recordings yet" body="Recordings of classes appear here for paid, active subscriptions." />
      ) : (
        <div className="grid gap-4 md:grid-cols-2">{data.map((r) => (
          <Panel key={r.id}><div data-testid={`card-recording-${r.id}`}>
            {playing === r.id ? <video src={storageUrl(r.objectPath)} controls autoPlay className="mb-3 aspect-video w-full rounded-lg bg-sidebar" data-testid={`video-${r.id}`} /> : (
              <button onClick={() => setPlaying(r.id)} className="mb-3 flex aspect-video w-full items-center justify-center rounded-lg bg-sidebar text-sidebar-primary transition hover:scale-[1.01]" data-testid={`button-play-${r.id}`}><span className="font-display text-xl font-bold">Play</span></button>
            )}
            <h3 className="font-display font-bold">{r.title}</h3>
            <p className="text-xs text-muted-foreground">{r.courseTitle} / {r.sessionTitle} / {fmtDur(r.durationSeconds)} / {fmtDay(r.uploadedAt)}</p>
          </div></Panel>
        ))}</div>
      )}
    </>
  );
}
