import { Routes } from '@angular/router';
import { Explore } from './explore/explore';
import { Create } from './create/create';

export const routes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'explore' },
  { path: 'explore', component: Explore, title: 'Explore quotes' },
  { path: 'create', component: Create, title: 'Add a quote' },
  { path: '**', redirectTo: 'explore' }
];
