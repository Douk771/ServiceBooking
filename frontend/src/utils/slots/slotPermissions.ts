/** Rights come from the server only (`myPermissions[]`); the menu is built from them, the server still checks every request. */
export function can<P extends string>(perms: readonly P[] | undefined, p: P): boolean {
  return !!perms && perms.includes(p)
}
