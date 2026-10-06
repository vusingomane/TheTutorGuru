import { useRequestUploadUrl } from '@workspace/api-client-react';
export function useFileUpload() {
  const req = useRequestUploadUrl();
  const mutateAsync = req.mutateAsync;
  return async (file: File) => {
    const contentType = file.type || 'application/octet-stream';
    const { uploadURL, objectPath } = await mutateAsync({ data: { name: file.name, size: file.size, contentType } });
    const r = await fetch(uploadURL, { method: 'PUT', body: file, headers: { 'Content-Type': contentType } });
    if (!r.ok) throw new Error('Upload failed. Please try again.');
    return { objectPath, contentType };
  };
}
export const storageUrl = (objectPath: string) => `/api/storage${objectPath}`;
