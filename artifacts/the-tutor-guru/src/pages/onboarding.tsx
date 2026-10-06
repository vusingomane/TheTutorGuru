import { useState } from 'react';
import { Redirect, useLocation } from 'wouter';
import { useQueryClient } from '@tanstack/react-query';
import { useSaveProfile, getGetProfileQueryKey, getGetOverviewQueryKey, type ProfileInputRole } from '@workspace/api-client-react';
import { Logo } from '@/components/logo';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { F, selectCls } from '@/components/kit';
import { useMe } from '@/hooks/use-me';
import { useToast } from '@/hooks/use-toast';
import { profileSaveError } from '@/lib/profile-error';

const roles: { v: ProfileInputRole; t: string; d: string }[] = [
  { v: 'parent', t: 'Parent', d: 'Add learners and manage subscriptions.' },
  { v: 'learner', t: 'Learner', d: 'Join classes and revise with notes.' },
  { v: 'tutor', t: 'Tutor', d: 'Run classes and share materials.' },
];
export default function Onboarding() {
  const { profile, isLoading } = useMe();
  const [, nav] = useLocation();
  const qc = useQueryClient();
  const { toast } = useToast();
  const save = useSaveProfile();
  const [role, setRole] = useState<ProfileInputRole>('parent');
  const [fullName, setName] = useState('');
  const [phone, setPhone] = useState('');
  const [grade, setGrade] = useState('10');
  const [saveError, setSaveError] = useState<string | null>(null);
  if (profile) return <Redirect to="/dashboard" />;
  if (isLoading) return null;
  const submit = (e: React.FormEvent) => {
    e.preventDefault();
    setSaveError(null);
    if (fullName.trim().length < 2) { toast({ title: 'Please enter your full name', variant: 'destructive' }); return; }
    save.mutate({ data: { fullName: fullName.trim(), role, phone: phone || null, grade: role === 'learner' ? Number(grade) : null } }, {
      onSuccess: (saved) => {
        qc.setQueryData(getGetProfileQueryKey(), saved);
        qc.invalidateQueries({ queryKey: getGetOverviewQueryKey() });
        nav('/dashboard');
      },
      onError: (error) => {
        const description = profileSaveError(error);
        setSaveError(description);
        toast({ title: 'Could not save profile', description, variant: 'destructive' });
      },
    });
  };
  return (
    <div className="grid-paper flex min-h-[100dvh] items-center justify-center px-4 py-10">
      <form onSubmit={submit} className="rise w-full max-w-lg space-y-6 rounded-2xl border bg-card p-7">
        <Logo />
        <div><h1 className="font-display text-3xl font-extrabold">Tell us who you are</h1><p className="text-sm text-muted-foreground">Your role is set once and cannot be changed later.</p></div>
        <div className="grid gap-2 sm:grid-cols-3">
          {roles.map((r) => (
            <button type="button" key={r.v} onClick={() => setRole(r.v)} data-testid={`button-role-${r.v}`}
              className={`rounded-xl border p-3 text-left transition ${role === r.v ? 'border-primary bg-primary text-primary-foreground' : 'hover:border-primary'}`}>
              <p className="font-display font-bold">{r.t}</p><p className="text-xs opacity-80">{r.d}</p>
            </button>
          ))}
        </div>
        <F label="Full name"><Input value={fullName} onChange={(e) => setName(e.target.value)} data-testid="input-fullname" /></F>
        <F label="Phone (optional)"><Input value={phone} onChange={(e) => setPhone(e.target.value)} data-testid="input-phone" /></F>
        {role === 'learner' && <F label="Grade"><select className={selectCls} value={grade} onChange={(e) => setGrade(e.target.value)} data-testid="select-grade">{[8, 9, 10, 11, 12].map((g) => <option key={g}>{g}</option>)}</select></F>}
        {saveError && <p role="alert" className="rounded-lg border border-destructive/40 bg-destructive/5 p-3 text-sm text-destructive">{saveError}</p>}
        <Button type="submit" className="w-full" disabled={save.isPending} data-testid="button-onboard-submit">{save.isPending ? 'Saving...' : 'Continue'}</Button>
      </form>
    </div>
  );
}
