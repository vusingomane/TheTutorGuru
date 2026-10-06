import { type ReactNode } from 'react';
import { AlertTriangle, Inbox } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { Skeleton } from '@/components/ui/skeleton';

export function PageHeader({ eyebrow, title, children }: { eyebrow?: string; title: string; children?: ReactNode }) {
  return (
    <div className="rise mb-8 flex flex-wrap items-end justify-between gap-4 border-b border-dashed pb-5">
      <div>
        {eyebrow && <p className="mb-1 font-mono text-xs uppercase tracking-widest text-protea">{eyebrow}</p>}
        <h1 className="font-display text-3xl font-extrabold tracking-tight md:text-4xl" data-testid="text-page-title">{title}</h1>
      </div>
      <div className="flex flex-wrap gap-2">{children}</div>
    </div>
  );
}
export function Empty({ title, body, action }: { title: string; body: string; action?: ReactNode }) {
  return (
    <div className="grid-paper flex flex-col items-center rounded-xl border border-dashed px-6 py-14 text-center" data-testid="empty-state">
      <div className="mb-3 rounded-full bg-secondary p-3 text-secondary-foreground"><Inbox className="h-5 w-5" /></div>
      <h3 className="font-display text-lg font-bold">{title}</h3>
      <p className="mt-1 max-w-sm text-sm text-muted-foreground">{body}</p>
      {action && <div className="mt-4">{action}</div>}
    </div>
  );
}
export function ErrorState({ onRetry }: { onRetry: () => void }) {
  return (
    <div className="flex flex-col items-center rounded-xl border border-destructive/30 bg-destructive/5 px-6 py-12 text-center" data-testid="error-state">
      <AlertTriangle className="mb-2 h-6 w-6 text-destructive" />
      <p className="font-display font-bold">We could not load this.</p>
      <p className="mb-4 text-sm text-muted-foreground">Check your connection and try again.</p>
      <Button variant="outline" onClick={onRetry} data-testid="button-retry">Retry</Button>
    </div>
  );
}
export function ListSkeleton({ rows = 3 }: { rows?: number }) {
  return <div className="space-y-3">{Array.from({ length: rows }).map((_, i) => <Skeleton key={i} className="h-20 w-full rounded-xl" />)}</div>;
}
export function Panel({ children, className = '' }: { children: ReactNode; className?: string }) {
  return <div className={`rounded-xl border bg-card p-5 transition-shadow hover:shadow-[0_6px_24px_-12px_hsl(178_50%_14%/.35)] ${className}`}>{children}</div>;
}
const tone: Record<string, string> = {
  active: 'bg-primary text-primary-foreground', paid: 'bg-primary text-primary-foreground', completed: 'bg-muted text-muted-foreground',
  pending: 'bg-secondary text-secondary-foreground', awaiting_provider: 'bg-secondary text-secondary-foreground', scheduled: 'bg-accent text-accent-foreground',
  live: 'bg-protea text-white', cancelled: 'bg-destructive/15 text-destructive', cancelling: 'bg-destructive/15 text-destructive', failed: 'bg-destructive/15 text-destructive',
};
export function Pill({ v }: { v: string }) {
  return <span className={`rounded-full px-2.5 py-0.5 font-mono text-[11px] uppercase tracking-wide ${tone[v] ?? 'bg-muted'}`} data-testid={`status-${v}`}>{v.replace('_', ' ')}</span>;
}
export const selectCls = 'h-10 w-full rounded-md border border-input bg-card px-3 text-sm focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring';
export function F({ label, children }: { label: string; children: ReactNode }) {
  return <label className="block space-y-1.5 text-sm font-medium"><span>{label}</span>{children}</label>;
}
