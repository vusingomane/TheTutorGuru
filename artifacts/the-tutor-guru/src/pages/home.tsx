import { Link } from 'wouter';
import { Check, Video, ShieldCheck, CalendarClock } from 'lucide-react';
import { useListSubjects } from '@workspace/api-client-react';
import { Logo } from '@/components/logo';
import { Button } from '@/components/ui/button';
import { rands } from '@/lib/format';

export default function Home() {
  const { data: subjects, isLoading, isError, refetch } = useListSubjects();
  return (
    <div className="min-h-[100dvh]">
      <header className="mx-auto flex max-w-6xl items-center justify-between px-5 py-5">
        <Logo />
        <div className="flex gap-2">
          <Button variant="ghost" asChild><Link href="/sign-in" data-testid="link-signin">Sign in</Link></Button>
          <Button asChild><Link href="/sign-up" data-testid="link-signup">Get started</Link></Button>
        </div>
      </header>
      <section className="grid-paper border-y">
        <div className="mx-auto grid max-w-6xl gap-10 px-5 py-16 md:grid-cols-[1.4fr_1fr] md:py-24">
          <div className="rise">
            <p className="mb-4 font-mono text-xs uppercase tracking-widest text-protea">Grades 8 to 12 / Live online / South Africa</p>
            <h1 className="font-display text-5xl font-extrabold leading-[0.98] tracking-tight md:text-7xl">
              The maths mark goes up when <span className="hl">someone shows up</span> every week.
            </h1>
            <p className="mt-6 max-w-xl text-lg text-muted-foreground">Two live one-hour classes a week with a tutor who knows your child by name. Parents see attendance. Learners get confidence. Tutors teach on a reliable timetable.</p>
            <div className="mt-8 flex flex-wrap gap-3">
              <Button size="lg" asChild><Link href="/sign-up" data-testid="button-hero-start">Start learning</Link></Button>
              <Button size="lg" variant="outline" asChild><a href="#pricing" data-testid="link-pricing">See pricing</a></Button>
            </div>
          </div>
          <div className="rise self-end rounded-2xl bg-primary p-6 text-primary-foreground [animation-delay:150ms]">
            <p className="font-mono text-xs uppercase tracking-widest text-secondary">A typical week</p>
            {[['Tue 17:00', 'Live class, 60 min'], ['Thu 17:00', 'Live class, 60 min'], ['Anytime', 'Notes and recordings']].map(([a, b]) => (
              <div key={a} className="mt-4 flex items-baseline justify-between border-b border-primary-foreground/20 pb-3"><span className="font-display text-xl font-bold">{a}</span><span className="text-sm opacity-80">{b}</span></div>
            ))}
            <p className="mt-4 text-xs opacity-70">Example schedule. Your tutor sets the real times.</p>
          </div>
        </div>
      </section>
      <section className="mx-auto grid max-w-6xl gap-6 px-5 py-16 md:grid-cols-3">
        {[[CalendarClock, 'Two classes a week', 'Every subject gets two one-hour live sessions, held on Teams or Zoom.'], [Video, 'Nothing missed', 'Notes and recordings sit beside the timetable for revision.'], [ShieldCheck, 'Parents in the loop', 'Attendance reports per learner, per subject, always current.']].map(([I, t, d], i) => {
          const Icon = I as typeof Video;
          return <div key={i} className={`rounded-xl border bg-card p-6 ${i === 1 ? 'md:translate-y-6' : ''}`}><Icon className="mb-3 h-6 w-6 text-protea" /><h3 className="font-display text-xl font-bold">{t as string}</h3><p className="mt-1 text-sm text-muted-foreground">{d as string}</p></div>;
        })}
      </section>
      <section id="pricing" className="bg-sidebar py-16 text-sidebar-foreground">
        <div className="mx-auto max-w-6xl px-5">
          <h2 className="font-display text-4xl font-extrabold">Simple monthly pricing</h2>
          <div className="mt-8 grid gap-5 md:grid-cols-2">
            {[['One subject', 290, 'Focus on the subject that worries you most.'], ['Two subjects', 495, 'Save R85 a month across two subjects.']].map(([n, p, d]) => (
              <div key={n as string} className="rounded-2xl border border-sidebar-border bg-sidebar-accent p-7" data-testid={`card-price-${p}`}>
                <p className="font-mono text-xs uppercase tracking-widest text-sidebar-primary">{n}</p>
                <p className="mt-2 font-display text-6xl font-extrabold">{rands(p as number)}<span className="text-lg font-medium opacity-70">/month</span></p>
                <p className="mt-2 text-sm opacity-80">{d}</p>
                <ul className="mt-5 space-y-2 text-sm">{['Two 1-hour live classes per week', 'Notes and recordings', 'Attendance reports', 'Full refund within 30 days'].map((x) => <li key={x} className="flex gap-2"><Check className="h-4 w-4 text-sidebar-primary" />{x}</li>)}</ul>
              </div>
            ))}
          </div>
          <p className="mt-5 text-sm opacity-80">Online payment is being connected. Subscription requests are recorded now and unlock once payment is live.</p>
        </div>
      </section>
      <section className="mx-auto max-w-6xl px-5 py-16">
        <h2 className="font-display text-4xl font-extrabold">Who is eligible, and for what</h2>
        <p className="mt-2 text-muted-foreground">Available to learners in Grades 8 to 12.</p>
        {isError ? <div className="mt-6"><Button variant="outline" onClick={() => refetch()} data-testid="button-retry-subjects">Could not load subjects. Retry</Button></div> : (
          <div className="mt-8 grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
            {isLoading ? Array.from({ length: 6 }).map((_, i) => <div key={i} className="h-28 animate-pulse rounded-xl bg-muted" />) : subjects?.map((s) => (
              <div key={s.id} className="rounded-xl border bg-card p-5" data-testid={`card-subject-${s.id}`}>
                <h3 className="font-display text-lg font-bold">{s.name}</h3>
                <p className="font-mono text-xs text-protea">Grades {s.grades.join(', ')}</p>
                {s.description && <p className="mt-1 text-sm text-muted-foreground">{s.description}</p>}
              </div>
            ))}
          </div>
        )}
      </section>
      <section className="mx-auto max-w-6xl px-5 pb-16">
        <div className="rounded-2xl bg-secondary p-8 md:flex md:items-center md:justify-between">
          <div><h2 className="font-display text-3xl font-extrabold text-secondary-foreground">30-day full refund</h2><p className="mt-1 text-secondary-foreground/80">If it is not working for your child, you get all of it back.</p></div>
          <Button size="lg" className="mt-4 md:mt-0" asChild><Link href="/sign-up" data-testid="button-cta-signup">Create your account</Link></Button>
        </div>
      </section>
      <footer className="border-t py-6 text-center text-xs text-muted-foreground">TheTutorGuru. Online high-school tutoring for South Africa.</footer>
    </div>
  );
}
