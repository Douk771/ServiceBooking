/** Роль CompanyOwner — это членство в компании: после создания добавляем её в сохранённого пользователя без перелогина. */
export function withOwnerRole(roles: readonly string[]): string[] {
  return roles.includes('CompanyOwner') ? [...roles] : [...roles, 'CompanyOwner']
}
