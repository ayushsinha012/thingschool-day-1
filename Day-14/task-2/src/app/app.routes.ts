import { Routes } from '@angular/router';
import { Explore } from './explore/explore';
import { Create } from './create/create';
import { CreateSignal } from './create-signal/create-signal';

export const routes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'explore' },
  { path: 'explore', component: Explore, title: 'Explore quotes' },
  { path: 'create', component: Create, title: 'Add a quote' },
  { path: 'create-signal', component: CreateSignal, title: 'Add a quote (Signal Forms)' },
  { path: '**', redirectTo: 'explore' }
];
