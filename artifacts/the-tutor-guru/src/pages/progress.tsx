import { useState } from 'react';
import { useListProgress, useListLearners, getListProgressQueryKey } from '@workspace/api-client-react';
import { PageHeader, ErrorState, ListSkeleton, Panel, Empty, selectCls } from '@/components/kit';
import { Progress } from '@/components/ui/progress';
import { useMe } from '@/hooks/use-me';
import { fmtDay } from '@/lib/format';

export default function ProgressPage() {
  const { role } = useMe();
  const [lid, setLid] = useState('');
  const learners = useListLearners({ query: { enabled: role !== 'learner', queryKey: ['/api/learners'] } });
  const params = lid ? { learnerId: lid } : undefined;
  const { data, isLoading, isError, refetch } = useListProgress(params, { query: { queryKey: getListProgressQueryKey(params) } });
  return (
    <>
      <PageHeader eyebrow="Attendance" title="Progress reports">
        {role !== 'learner' && <select className={`${selectCls} w-56`} value={lid} onChange={(e) => setLid(e.target.value)} data-testid="select-learner-filter"><option value="">All learners</option>{learners.data?.map((l) => <option key={l.id} value={l.id}>{l.name}</option>)}</select>}
      </PageHeader>
      {isError ? <ErrorState onRetry={() => refetch()} /> : isLoading ? <ListSkeleton /> : !data?.length ? (
        <Empty title="No reports yet" body="Attendance reports build up as classes are held and marked." />
      ) : (
        <div className="grid gap-4 md:grid-cols-2">{data.map((p) => (
          <Panel key={`${p.learnerId}-${p.subjectId}`}><div data-testid={`card-progress-${p.learnerId}-${p.subjectId}`}>
            <p className="font-mono text-xs uppercase text-protea">{p.subjectName}</p>
            <h3 className="font-display text-lg font-bold">{p.learnerName} <span className="text-sm font-normal text-muted-foreground">Grade {p.grade}</span></h3>
            <div className="mt-3 flex items-baseline justify-between"><span className="font-display text-4xl font-extrabold">{Math.round(p.attendancePercent)}%</span><span className="text-sm text-muted-foreground">{p.classesAttended} of {p.classesTotal} classes</span></div>
            <Progress value={p.attendancePercent} className="mt-2 h-2" />
            <p className="mt-3 text-xs text-muted-foreground">{p.averageScore != null ? `Average score ${p.averageScore}. ` : ''}Updated {fmtDay(p.updatedAt)}</p>
          </div></Panel>
        ))}</div>
      )}
    </>
  );
}
