/** The few Monaco internals `monaco.ts` uses; the ESM build ships no types for them. */
declare module 'monaco-editor/platform/actions/common/actions' {
  export class MenuId {
    static readonly EditorContext: MenuId;
  }
  export const MenuRegistry: {
    getMenuItems(id: MenuId): ({ command?: { id: string } } & object)[];
  };
}
