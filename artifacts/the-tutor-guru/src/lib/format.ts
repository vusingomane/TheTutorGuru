import { format } from 'date-fns';
export const rands = (n: number) => `R${n.toLocaleString('en-ZA')}`;
export const fmtDate = (s?: string | null) => (s ? format(new Date(s), 'EEE d MMM, HH:mm') : '-');
export const fmtDay = (s?: string | null) => (s ? format(new Date(s), 'd MMM yyyy') : '-');
export const priceFor = (n: number) => (n <= 0 ? 0 : n === 1 ? 290 : 495);
export const fmtSize = (b: number) => (b > 1048576 ? `${(b / 1048576).toFixed(1)} MB` : `${Math.max(1, Math.round(b / 1024))} KB`);
export const fmtDur = (s: number) => `${Math.floor(s / 60)}m ${String(s % 60).padStart(2, '0')}s`;
export const toLocalInput = (s: string) => { const d = new Date(s); d.setMinutes(d.getMinutes() - d.getTimezoneOffset()); return d.toISOString().slice(0, 16); };
