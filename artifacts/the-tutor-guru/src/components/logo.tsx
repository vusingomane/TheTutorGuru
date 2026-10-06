import { Link } from 'wouter';
export function Logo({ light = false }: { light?: boolean }) {
  return (
    <Link href="/" className="flex items-center gap-2.5" data-testid="link-logo">
      <img src={`${import.meta.env.BASE_URL}logo.svg`} alt="" className="h-8 w-8" />
      <span className={`font-display text-lg font-extrabold tracking-tight ${light ? 'text-sidebar-foreground' : 'text-foreground'}`}>
        TheTutor<span className="text-protea">Guru</span>
      </span>
    </Link>
  );
}
