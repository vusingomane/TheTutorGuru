export function profileSaveError(error: unknown): string {
  if (error && typeof error === 'object') {
    const response = error as { status?: number; data?: { error?: unknown } };
    if (response.status === 401) return 'Your session has expired. Please sign in again before saving your profile.';
    if (typeof response.data?.error === 'string') return response.data.error;
  }
  return 'We could not save your profile. Check your connection and try again.';
}
