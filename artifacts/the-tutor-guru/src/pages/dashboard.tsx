import { useGetOverview, getGetOverviewQueryKey } from '@workspace/api-client-react';
import { PageHeader, ErrorState, ListSkeleton, Panel, Empty } from '@/components/kit';
import { useMe } from '@/hooks/use-me';
import { fmtDate } from '@/lib/format';

export default function Dashboard() {
  const { profile } = useMe();
  const { data, isLoading, isError, refetch } = useGetOverview({ query: { queryKey: getGetOverviewQueryKey() } });
  const stats = data ? [['Upcoming classes', data.upcomingSessions], ['Active subjects', data.activeSubjects], [data.role === 'tutor' ? 'Learners taught' : 'Learners', data.totalLearners]] : [];
  return (
    <>
      <PageHeader eyebrow={data?.role ?? 'overview'} title={`Hello, ${profile?.fullName.split(' ')[0] ?? ''}`} />
      {isError ? <ErrorState onRetry={() => refetch()} /> : isLoading || !data ? <ListSkeleton /> : (
        <>
          <div className="grid gap-4 sm:grid-cols-3">
            {stats.map(([l, v], i) => (
              <Panel key={l as string} className={i === 0 ? 'bg-primary text-primary-foreground' : ''}>
                <p className="font-mono text-xs uppercase tracking-widest opacity-70">{l}</p>
                <p className="font-display text-5xl font-extrabold" data-testid={`stat-${i}`}>{v}</p>
              </Panel>
            ))}
          </div>
          <h2 className="mb-3 mt-10 font-display text-xl font-bold">Recent activity</h2>
          {data.recentActivity.length === 0 ? <Empty title="Nothing yet" body="Activity from classes, materials and subscriptions will appear here." /> : (
            <ul className="space-y-2">{data.recentActivity.map((a) => (
              <li key={a.id} className="rounded-xl border bg-card p-4" data-testid={`row-activity-${a.id}`}><p className="font-medium">{a.title}</p><p className="text-sm text-muted-foreground">{a.detail}</p><p className="mt-1 font-mono text-xs text-muted-foreground">{fmtDate(a.createdAt)}</p></li>
            ))}</ul>
          )}
        </>
      )}
    </>
  );
}
