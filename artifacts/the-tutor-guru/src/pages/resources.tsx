import { useRef, useState } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { Download, FileText, Trash2 } from 'lucide-react';
import { useListMaterials, useCreateMaterial, useDeleteMaterial, useListCourses, getListMaterialsQueryKey, getGetOverviewQueryKey } from '@workspace/api-client-react';
import { PageHeader, ErrorState, ListSkeleton, Panel, Empty, F, selectCls } from '@/components/kit';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Dialog, DialogContent, DialogHeader, DialogTitle, DialogTrigger } from '@/components/ui/dialog';
import { useMe } from '@/hooks/use-me';
import { useToast } from '@/hooks/use-toast';
import { useFileUpload, storageUrl } from '@/lib/upload';
import { fmtSize, fmtDay } from '@/lib/format';

function Upload() {
  const courses = useListCourses();
  const upload = useFileUpload();
  const create = useCreateMaterial();
  const qc = useQueryClient();
  const { toast } = useToast();
  const [open, setOpen] = useState(false);
  const [busy, setBusy] = useState(false);
  const [courseId, setCourse] = useState('');
  const [title, setTitle] = useState('');
  const file = useRef<HTMLInputElement>(null);
  const submit = async (e: React.FormEvent) => {
    e.preventDefault();
    const f = file.current?.files?.[0];
    if (!f) return;
    setBusy(true);
    try {
      const { objectPath, contentType } = await upload(f);
      create.mutate({ data: { courseId, title: title.trim(), originalFileName: f.name.slice(0, 255), objectPath, contentType, sizeBytes: Math.max(1, f.size) } }, {
        onSuccess: () => { qc.invalidateQueries({ queryKey: getListMaterialsQueryKey() }); qc.invalidateQueries({ queryKey: getGetOverviewQueryKey() }); toast({ title: 'Notes uploaded' }); setOpen(false); setTitle(''); },
        onError: () => toast({ title: 'Saved file but could not register it', variant: 'destructive' }),
      });
    } catch (err) { toast({ title: err instanceof Error ? err.message : 'Upload failed', variant: 'destructive' }); }
    setBusy(false);
  };
  return (
    <Dialog open={open} onOpenChange={setOpen}>
      <DialogTrigger asChild><Button data-testid="button-upload-material">Upload notes</Button></DialogTrigger>
      <DialogContent><DialogHeader><DialogTitle>Upload notes</DialogTitle></DialogHeader>
        <form onSubmit={submit} className="space-y-4">
          <F label="Course"><select required className={selectCls} value={courseId} onChange={(e) => setCourse(e.target.value)} data-testid="select-material-course"><option value="">Choose course</option>{courses.data?.map((c) => <option key={c.id} value={c.id}>{c.title}</option>)}</select></F>
          <F label="Title"><Input required minLength={2} value={title} onChange={(e) => setTitle(e.target.value)} data-testid="input-material-title" /></F>
          <F label="File"><Input required type="file" ref={file} data-testid="input-material-file" /></F>
          <Button type="submit" disabled={busy || create.isPending} data-testid="button-save-material">{busy ? 'Uploading...' : 'Upload'}</Button>
        </form>
      </DialogContent>
    </Dialog>
  );
}
export default function Resources() {
  const { role } = useMe();
  const { data, isLoading, isError, refetch } = useListMaterials();
  const del = useDeleteMaterial();
  const qc = useQueryClient();
  const { toast } = useToast();
  const [confirm, setConfirm] = useState('');
  const remove = (id: string) => del.mutate({ materialId: id }, {
    onSuccess: () => { qc.invalidateQueries({ queryKey: getListMaterialsQueryKey() }); qc.invalidateQueries({ queryKey: getGetOverviewQueryKey() }); toast({ title: 'Material removed' }); setConfirm(''); },
    onError: () => toast({ title: 'Could not remove', variant: 'destructive' }),
  });
  return (
    <>
      <PageHeader eyebrow="Library" title="Resources">{role === 'tutor' && <Upload />}</PageHeader>
      {isError ? <ErrorState onRetry={() => refetch()} /> : isLoading ? <ListSkeleton /> : !data?.length ? (
        <Empty title="No notes available" body={role === 'tutor' ? 'Upload notes for one of your courses.' : 'Notes appear here once you have a paid, active subscription and your tutor shares them.'} />
      ) : (
        <div className="space-y-3">{data.map((m) => (
          <Panel key={m.id}><div className="flex items-center gap-4" data-testid={`row-material-${m.id}`}>
            <div className="rounded-lg bg-secondary p-3"><FileText className="h-5 w-5" /></div>
            <div className="min-w-0 flex-1"><p className="truncate font-display font-bold">{m.title}</p><p className="truncate text-xs text-muted-foreground">{m.courseTitle} / {m.originalFileName} / {fmtSize(m.sizeBytes)} / {fmtDay(m.uploadedAt)}</p></div>
            <Button size="sm" variant="outline" asChild><a href={storageUrl(m.objectPath)} download={m.originalFileName} data-testid={`link-download-${m.id}`}><Download className="mr-1 h-4 w-4" />Download</a></Button>
            {role === 'tutor' && (confirm === m.id
              ? <Button size="sm" variant="destructive" disabled={del.isPending} onClick={() => remove(m.id)} data-testid={`button-confirm-delete-${m.id}`}>Confirm</Button>
              : <Button size="icon" variant="ghost" onClick={() => setConfirm(m.id)} aria-label="Remove" data-testid={`button-delete-${m.id}`}><Trash2 className="h-4 w-4" /></Button>)}
          </div></Panel>
        ))}</div>
      )}
    </>
  );
}
