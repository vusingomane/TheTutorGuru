import { useState, type ReactNode } from 'react';
import { Link, useLocation, Redirect } from 'wouter';
import { useClerk } from '@clerk/react';
import { LayoutDashboard, BookOpen, CreditCard, CalendarDays, FileText, Video, TrendingUp, Users, Settings, LogOut, Menu, X } from 'lucide-react';
import { Logo } from '@/components/logo';
import { useMe } from '@/hooks/use-me';
import { Skeleton } from '@/components/ui/skeleton';

const nav = [
  { href: '/dashboard', label: 'Dashboard', icon: LayoutDashboard },
  { href: '/subjects', label: 'Subjects', icon: BookOpen },
  { href: '/subscriptions', label: 'Subscriptions', icon: CreditCard, roles: ['parent'] },
  { href: '/classes', label: 'Classes', icon: CalendarDays },
  { href: '/resources', label: 'Resources', icon: FileText },
  { href: '/recordings', label: 'Recordings', icon: Video },
  { href: '/progress', label: 'Progress', icon: TrendingUp },
  { href: '/learners', label: 'Learners', icon: Users, roles: ['parent', 'tutor'] },
  { href: '/settings', label: 'Settings', icon: Settings },
];
export function AppShell({ children }: { children: ReactNode }) {
  const { profile, role, isLoading, isError, error, refetch } = useMe();
  const [loc] = useLocation();
  const [open, setOpen] = useState(false);
  const { signOut } = useClerk();
  if (isError && error?.status === 404) return <Redirect to="/onboarding" />;
  if (isError) return <div className="flex min-h-screen flex-col items-center justify-center gap-4 p-6"><Logo /><h1 className="text-xl font-bold">We couldn't load your account.</h1><p>Please retry rather than creating a new profile.</p><button className="rounded-lg bg-primary px-5 py-3 text-primary-foreground" onClick={() => refetch()}>Try again</button></div>;
  const items = nav.filter((n) => !n.roles || (role && n.roles.includes(role)));
  const side = (
    <div className="flex h-full flex-col gap-6 p-5">
      <Logo light />
      <nav className="flex-1 space-y-1">
        {isLoading ? <Skeleton className="h-40 bg-sidebar-accent" /> : items.map((n) => {
          const on = loc.startsWith(n.href);
          return (
            <Link key={n.href} href={n.href} onClick={() => setOpen(false)} data-testid={`link-nav-${n.label.toLowerCase()}`}
              className={`flex items-center gap-3 rounded-lg px-3 py-2.5 text-sm font-medium transition-all hover:translate-x-0.5 ${on ? 'bg-sidebar-primary text-sidebar-primary-foreground' : 'text-sidebar-foreground/80 hover:bg-sidebar-accent'}`}>
              <n.icon className="h-4 w-4" />{n.label}
            </Link>
          );
        })}
      </nav>
      <div className="rounded-lg bg-sidebar-accent p-3 text-sidebar-foreground">
        <p className="truncate text-sm font-bold" data-testid="text-user-name">{profile?.fullName ?? '...'}</p>
        <p className="font-mono text-[11px] uppercase tracking-widest text-sidebar-primary" data-testid="text-user-role">{role}</p>
        <button onClick={() => signOut({ redirectUrl: import.meta.env.BASE_URL || '/' })} className="mt-2 flex items-center gap-1.5 text-xs opacity-80 hover:opacity-100" data-testid="button-signout"><LogOut className="h-3.5 w-3.5" />Sign out</button>
      </div>
    </div>
  );
  return (
    <div className="min-h-[100dvh] md:grid md:grid-cols-[250px_1fr]">
      <aside className="sticky top-0 hidden h-[100dvh] bg-sidebar md:block">{side}</aside>
      <div className="flex items-center justify-between bg-sidebar px-4 py-3 md:hidden">
        <Logo light />
        <button onClick={() => setOpen(!open)} className="text-sidebar-foreground" aria-label="Menu" data-testid="button-menu">{open ? <X /> : <Menu />}</button>
      </div>
      {open && <div className="fixed inset-x-0 bottom-0 top-[56px] z-40 overflow-auto bg-sidebar md:hidden">{side}</div>}
      <main className="min-w-0 px-4 py-8 md:px-10"><div className="mx-auto max-w-5xl">{children}</div></main>
    </div>
  );
}
