export interface Company {
  id: string
  name: string
  slug: string
  description?: string
  logoUrl?: string
  address?: string
  phone?: string
  email?: string
  allowSelfBooking: boolean
}

export interface Service {
  id: string
  companyId: string
  name: string
  description?: string
  durationMinutes: number
  price: number
  imageUrl?: string
}

export interface Master {
  id: string
  firstName: string
  lastName: string
  avatarUrl?: string
  bio?: string
  role: string
}

export interface TimeSlot {
  start: string
  end: string
}

export interface Booking {
  id: string
  companyId: string
  serviceId: string
  serviceName: string
  masterId: string
  masterName: string
  clientId?: string
  clientName: string
  clientPhone?: string
  clientEmail?: string
  date: string
  startTime: string
  endTime: string
  status: BookingStatus
  notes?: string
  createdAt: string
}

export type BookingStatus = 'Pending' | 'Confirmed' | 'Cancelled' | 'Completed' | 'NoShow'

export interface AuthResponse {
  token: string
  userId: string
  email: string
  firstName: string
  lastName: string
  roles: string[]
}

export interface User {
  id: string
  email: string
  firstName: string
  lastName: string
  roles: string[]
}
