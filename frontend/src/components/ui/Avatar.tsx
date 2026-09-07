interface AvatarProps {
  avatarUrl?: string | null
  firstName: string
  lastName?: string
  size?: number
  className?: string
}

/**
 * Shows the user's photo when `avatarUrl` is set (US-25), otherwise the existing initials placeholder.
 * Avatars are public (`wwwroot/uploads/avatars/...`), so a plain `<img src>` works here — unlike client
 * note photos, which are private and go through `AuthedImage` instead.
 */
export function Avatar({ avatarUrl, firstName, lastName = '', size = 40, className = '' }: AvatarProps) {
  const style = { width: size, height: size }
  if (avatarUrl) {
    return (
      <img
        src={avatarUrl}
        alt={`${firstName} ${lastName}`.trim()}
        style={style}
        className={`rounded-full object-cover shrink-0 ${className}`}
      />
    )
  }
  return (
    <div
      style={style}
      className={`rounded-full bg-cream-deep flex items-center justify-center text-gold-dark font-bold shrink-0 ${className}`}
    >
      <span style={{ fontSize: size * 0.36 }}>{firstName[0]?.toUpperCase()}{lastName[0]?.toUpperCase() ?? ''}</span>
    </div>
  )
}
