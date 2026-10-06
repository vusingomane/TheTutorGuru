import { useGetProfile, getGetProfileQueryKey } from '@workspace/api-client-react';
export function useMe() {
  const q = useGetProfile({ query: { retry: false, queryKey: getGetProfileQueryKey(), staleTime: 60000 } });
  return { profile: q.data, role: q.data?.role, isLoading: q.isLoading, isError: q.isError, error: q.error, refetch: q.refetch };
}
