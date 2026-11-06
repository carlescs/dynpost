import { Routes } from '@angular/router';

/**
 * One lazy-loaded module per activity-bar view. The route picks the panel shown in the sidebar;
 * the editor area is shared by all of them and stays in the shell.
 */
export const routes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'explorer' },
  {
    path: 'explorer',
    loadChildren: () => import('./modules/explorer/explorer.routes').then((m) => m.EXPLORER_ROUTES),
  },
  {
    path: 'environments',
    loadChildren: () =>
      import('./modules/environments/environments.routes').then((m) => m.ENVIRONMENTS_ROUTES),
  },
  {
    path: 'git',
    loadChildren: () => import('./modules/git/git.routes').then((m) => m.GIT_ROUTES),
  },
  { path: '**', redirectTo: 'explorer' },
];
