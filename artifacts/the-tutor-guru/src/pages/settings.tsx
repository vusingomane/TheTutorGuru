import { useEffect, useState } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { useSaveProfile, getGetProfileQueryKey, getGetOverviewQueryKey } from '@workspace/api-client-react';
import { PageHeader, F, selectCls, ListSkeleton } from '@/components/kit';
import { Input } from '@/components/ui/input';
import { Button } from '@/components/ui/button';
import { useMe } from '@/hooks/use-me';
import { useToast } from '@/hooks/use-toast';
import { profileSaveError } from '@/lib/profile-error';

export default function Settings() {
  const { profile } = useMe();
  const qc = useQueryClient();
  const { toast } = useToast();
  const save = useSaveProfile();
  const [name, setName] = useState('');
  const [phone, setPhone] = useState('');
  const [grade, setGrade] = useState('10');
  const [saveError, setSaveError] = useState<string | null>(null);
  useEffect(() => { if (profile) { setName(profile.fullName); setPhone(profile.phone ?? ''); setGrade(String(profile.grade ?? 10)); } }, [profile?.id]);
  if (!profile) return <ListSkeleton />;
  const submit = (e: React.FormEvent) => {
    e.preventDefault();
    setSaveError(null);
    if (name.trim().length < 2) { setSaveError('Please enter your full name.'); return; }
    save.mutate({ data: { fullName: name.trim(), role: profile.role, phone: phone || null, grade: profile.role === 'learner' ? Number(grade) : null } }, {
      onSuccess: (saved) => { qc.setQueryData(getGetProfileQueryKey(), saved); qc.invalidateQueries({ queryKey: getGetOverviewQueryKey() }); toast({ title: 'Profile saved' }); },
      onError: (error) => {
        const description = profileSaveError(error);
        setSaveError(description);
        toast({ title: 'Could not save profile', description, variant: 'destructive' });
      },
    });
  };
  return (
    <>
      <PageHeader eyebrow="Account" title="Settings" />
      <form onSubmit={submit} className="max-w-lg space-y-5 rounded-xl border bg-card p-6">
        {saveError && <p role="alert" className="rounded-lg border border-destructive/40 bg-destructive/5 p-3 text-sm text-destructive">{saveError}</p>}
        <F label="Email"><Input value={profile.email} disabled data-testid="input-email" /></F>
        <F label="Role"><Input value={profile.role} disabled className="capitalize" data-testid="input-role" /></F>
        <F label="Full name"><Input value={name} onChange={(e) => setName(e.target.value)} data-testid="input-name" /></F>
        <F label="Phone"><Input value={phone} onChange={(e) => setPhone(e.target.value)} data-testid="input-phone" /></F>
        {profile.role === 'learner' && <F label="Grade"><select className={selectCls} value={grade} onChange={(e) => setGrade(e.target.value)}>{[8, 9, 10, 11, 12].map((g) => <option key={g}>{g}</option>)}</select></F>}
        <Button type="submit" disabled={save.isPending} data-testid="button-save-profile">{save.isPending ? 'Saving...' : 'Save changes'}</Button>
      </form>
    </>
  );
}
