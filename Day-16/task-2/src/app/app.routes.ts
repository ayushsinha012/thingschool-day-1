import { Routes } from '@angular/router';
import { Explore } from './explore/explore';
import { Create } from './create/create';
import { CreateSignal } from './create-signal/create-signal';
import { HttpLab } from './http-lab/http-lab';
import { authGuard } from './auth.guard';

export const routes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'explore' },
  { path: 'explore', component: Explore, title: 'Explore quotes' },
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
  { path: '**', redirectTo: 'explore' }
];
