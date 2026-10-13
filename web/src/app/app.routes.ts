import { Routes } from '@angular/router';

// The kiosk opens /tv; a phone that just types the box's address lands on the remote.
export const routes: Routes = [
  { path: 'tv', loadComponent: () => import('./tv/tv-screen').then(m => m.TvScreen) },
  { path: 'remote', loadComponent: () => import('./remote/remote').then(m => m.Remote) },
  { path: '', pathMatch: 'full', redirectTo: 'remote' },
  { path: '**', redirectTo: 'remote' },
];
