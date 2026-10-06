import { useEffect, useRef, type ComponentType } from 'react';
import { ClerkProvider, SignIn, SignUp, Show, useClerk } from '@clerk/react';
import { publishableKeyFromHost } from '@clerk/react/internal';
import { shadcn } from '@clerk/themes';
import { QueryClient, QueryClientProvider, useQueryClient } from '@tanstack/react-query';
import { Redirect, Route, Switch, useLocation, Router as WouterRouter } from 'wouter';
import { ErrorBoundary } from '@/components/error-boundary';
import { Toaster } from '@/components/ui/toaster';
import { TooltipProvider } from '@/components/ui/tooltip';
import { AppShell } from '@/components/app-shell';
import { Logo } from '@/components/logo';
import NotFound from '@/pages/not-found';
import Home from '@/pages/home';
import Onboarding from '@/pages/onboarding';
import Dashboard from '@/pages/dashboard';
import Subjects from '@/pages/subjects';
import Subscriptions from '@/pages/subscriptions';
import Classes from '@/pages/classes';
import Resources from '@/pages/resources';
import Recordings from '@/pages/recordings';
import ProgressPage from '@/pages/progress';
import Learners from '@/pages/learners';
import Settings from '@/pages/settings';

const queryClient = new QueryClient({ defaultOptions: { queries: { refetchOnWindowFocus: false } } });

const clerkPubKey = publishableKeyFromHost(window.location.hostname, import.meta.env.VITE_CLERK_PUBLISHABLE_KEY);
const clerkProxyUrl = import.meta.env.VITE_CLERK_PROXY_URL;
const basePath = import.meta.env.BASE_URL.replace(/\/$/, '');

function stripBase(path: string): string {
  return basePath && path.startsWith(basePath) ? path.slice(basePath.length) || '/' : path;
}
if (!clerkPubKey) throw new Error('Missing VITE_CLERK_PUBLISHABLE_KEY in .env file');

const clerkAppearance = {
  theme: shadcn,
  cssLayerName: 'clerk',
  options: { logoPlacement: 'inside' as const, logoLinkUrl: basePath || '/', logoImageUrl: `${window.location.origin}${basePath}/logo.svg` },
  variables: {
    colorPrimary: 'hsl(178 55% 19%)', colorForeground: 'hsl(178 40% 12%)', colorMutedForeground: 'hsl(178 15% 34%)', colorDanger: 'hsl(0 65% 45%)',
    colorBackground: 'hsl(44 55% 98%)', colorInput: 'hsl(42 45% 95%)', colorInputForeground: 'hsl(178 40% 12%)', colorNeutral: 'hsl(178 40% 12%)',
    fontFamily: "'DM Sans', sans-serif", borderRadius: '0.6rem',
  },
  elements: {
    rootBox: 'w-full flex justify-center',
    cardBox: 'bg-[hsl(44_55%_98%)] border border-[hsl(40_25%_82%)] rounded-2xl w-[440px] max-w-full overflow-hidden shadow-xl',
    card: '!shadow-none !border-0 !bg-transparent !rounded-none',
    footer: '!shadow-none !border-0 !bg-transparent !rounded-none',
    headerTitle: { fontFamily: "'Bricolage Grotesque', sans-serif", fontWeight: 800, color: 'hsl(178 40% 12%)' },
    headerSubtitle: { color: 'hsl(178 15% 34%)' },
    socialButtonsBlockButtonText: { color: 'hsl(178 40% 12%)' },
    formFieldLabel: { color: 'hsl(178 40% 12%)' },
    footerActionLink: { color: 'hsl(350 65% 40%)', fontWeight: 700 },
    footerActionText: { color: 'hsl(178 15% 34%)' },
    dividerText: { color: 'hsl(178 15% 34%)' },
    identityPreviewEditButton: { color: 'hsl(350 65% 40%)' },
    formFieldSuccessText: { color: 'hsl(178 55% 25%)' },
    alertText: { color: 'hsl(0 65% 35%)' },
    formButtonPrimary: { backgroundColor: 'hsl(178 55% 19%)', color: 'hsl(42 50% 96%)' },
  },
};

function SignInPage() {
  return <div className="grid-paper flex min-h-[100dvh] flex-col items-center justify-center gap-6 px-4 py-8"><SignIn routing="path" path={`${basePath}/sign-in`} signUpUrl={`${basePath}/sign-up`} /></div>;
}
function SignUpPage() {
  return <div className="grid-paper flex min-h-[100dvh] flex-col items-center justify-center gap-6 px-4 py-8"><SignUp routing="path" path={`${basePath}/sign-up`} signInUrl={`${basePath}/sign-in`} /></div>;
}
function HomeRedirect() {
  return (
    <>
      <Show when="signed-in"><Redirect to="/dashboard" /></Show>
      <Show when="signed-out"><Home /></Show>
    </>
  );
}
function Guard({ page: Page, shell = true }: { page: ComponentType; shell?: boolean }) {
  return (
    <>
      <Show when="signed-in">{shell ? <AppShell><Page /></AppShell> : <Page />}</Show>
      <Show when="signed-out"><Redirect to="/sign-in" /></Show>
    </>
  );
}
const g = (page: ComponentType, shell = true) => () => <Guard page={page} shell={shell} />;

function ClerkQueryClientCacheInvalidator() {
  const { addListener } = useClerk();
  const qc = useQueryClient();
  const prev = useRef<string | null | undefined>(undefined);
  useEffect(() => addListener(({ user }) => {
    const id = user?.id ?? null;
    if (prev.current !== undefined && prev.current !== id) qc.clear();
    prev.current = id;
  }), [addListener, qc]);
  return null;
}

function ClerkProviderWithRoutes() {
  const [location, setLocation] = useLocation();
  return (
    <ClerkProvider
      publishableKey={clerkPubKey}
      proxyUrl={clerkProxyUrl}
      appearance={clerkAppearance}
      signInUrl={`${basePath}/sign-in`}
      signUpUrl={`${basePath}/sign-up`}
      localization={{
        signIn: { start: { title: 'Welcome back', subtitle: 'Sign in to your TheTutorGuru account' } },
        signUp: { start: { title: 'Join TheTutorGuru', subtitle: 'Parents, learners and tutors start here' } },
      }}
      routerPush={(to) => setLocation(stripBase(to))}
      routerReplace={(to) => setLocation(stripBase(to), { replace: true })}
    >
      <QueryClientProvider client={queryClient}>
        <TooltipProvider>
          <ClerkQueryClientCacheInvalidator />
          <ErrorBoundary resetKey={location}>
            <Switch>
              <Route path="/" component={HomeRedirect} />
              <Route path="/sign-in/*?" component={SignInPage} />
              <Route path="/sign-up/*?" component={SignUpPage} />
              <Route path="/onboarding" component={g(Onboarding, false)} />
              <Route path="/dashboard" component={g(Dashboard)} />
              <Route path="/subjects" component={g(Subjects)} />
              <Route path="/subscriptions" component={g(Subscriptions)} />
              <Route path="/classes" component={g(Classes)} />
              <Route path="/resources" component={g(Resources)} />
              <Route path="/recordings" component={g(Recordings)} />
              <Route path="/progress" component={g(ProgressPage)} />
              <Route path="/learners" component={g(Learners)} />
              <Route path="/settings" component={g(Settings)} />
              <Route component={NotFound} />
            </Switch>
          </ErrorBoundary>
          <Toaster />
        </TooltipProvider>
      </QueryClientProvider>
    </ClerkProvider>
  );
}

function App() {
  return (
    <WouterRouter base={basePath}>
      <ClerkProviderWithRoutes />
    </WouterRouter>
  );
}

export default App;
