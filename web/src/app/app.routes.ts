import { Routes } from '@angular/router';

// The kiosk opens /tv; a phone that just types the box's address lands on the remote.
export const routes: Routes = [
  { path: 'admin', title: 'Channel lineup · Rabbit Ears', loadComponent: () => import('./admin/admin').then(m => m.Admin) },
  { path: 'tv', title: 'TV · Rabbit Ears', loadComponent: () => import('./tv/tv-screen').then(m => m.TvScreen) },
  // The public demo site opens here: the TV, a QR code for a phone remote, and a remote on the page.
  { path: 'demo', title: 'Rabbit Ears: old shows on a 90s TV', loadComponent: () => import('./demo/demo').then(m => m.Demo) },
  { path: 'remote', title: 'Remote · Rabbit Ears', loadComponent: () => import('./remote/remote').then(m => m.Remote) },
  { path: '', pathMatch: 'full', redirectTo: 'remote' },
  { path: '**', redirectTo: 'remote' },
];
