import { Routes } from '@angular/router';
import { Explore } from './explore/explore';
import { Create } from './create/create';
import { CreateSignal } from './create-signal/create-signal';
import { HttpLab } from './http-lab/http-lab';
import { Login } from './login/login';
import { Signup } from './signup/signup';
import { authGuard } from './auth.guard';

export const routes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'explore' },
  { path: 'explore', component: Explore, title: 'Explore quotes' },
  { path: 'login', component: Login, title: 'Log in' },
  { path: 'signup', component: Signup, title: 'Sign up' },
  // Detail is the only newly added route, so it's the only one lazy-loaded here
  // (Day 16's scope) - loadComponent means its chunk (and QuoteDetailPage's own
  // template/styles) is fetched only when a user actually navigates to a quote,
  // not bundled into the eagerly-loaded initial chunk with Explore/Create/etc.
  {
    path: 'quotes/:id',
    loadComponent: () => import('./quote-detail/quote-detail').then((m) => m.QuoteDetailPage),
    title: 'Quote detail'
  },
  // Guarded the same way QuotesApi itself guards writes: POST /api/quotes
  // requires PermissionClaims.CanEditQuotes (see auth.guard.ts). GET
  // /api/quotes/{id} above is anonymous on the backend, so the detail route
  // is intentionally left unguarded.
  { path: 'create', component: Create, title: 'Add a quote', canActivate: [authGuard] },
  {
    path: 'create-signal',
    component: CreateSignal,
    title: 'Add a quote (Signal Forms)',
    canActivate: [authGuard]
  },
  { path: 'http-lab', component: HttpLab, title: 'HTTP Lab' },
  // POST /api/jobs is anonymous on the backend (see JobEndpoints), same as
  // the GET quote endpoints - no authGuard needed here.
  {
    path: 'jobs',
    loadComponent: () => import('./jobs/jobs').then((m) => m.Jobs),
    title: 'Background Jobs'
  },
  {
    path: 'messaging',
    loadComponent: () => import('./messaging/messaging').then((m) => m.Messaging),
    title: 'Messaging'
  },
  { path: '**', redirectTo: 'explore' }
];
