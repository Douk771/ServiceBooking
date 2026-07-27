/** @type {import('tailwindcss').Config} */
export default {
  content: ['./index.html', './src/**/*.{js,ts,jsx,tsx}'],
  theme: {
    extend: {
      colors: {
        cream: {
          DEFAULT: '#FAF6F0',
          deep: '#F1E9DC',
        },
        ink: {
          DEFAULT: '#2B2420',
          soft: '#6B5F52',
        },
        muted: '#A69A88',
        line: {
          DEFAULT: '#E6DCCB',
          strong: '#D8C9AE',
        },
        gold: {
          DEFAULT: '#A9835A',
          dark: '#8F6C46',
        },
        success: { DEFAULT: '#4B6B4B', bg: '#E9F1E6' },
        danger: { DEFAULT: '#A6503A', bg: '#FBEAE4' },
        warning: { DEFAULT: '#8A6A2E', bg: '#FBF3E1' },
        info: { DEFAULT: '#52667A', bg: '#E9EEF3' },
        // Legacy scale kept so any not-yet-restyled component still lands inside the new
        // cream/ink/gold palette instead of the old orange, rather than being migrated by hand.
        primary: {
          50: '#FAF6F0',
          100: '#F1E9DC',
          200: '#E6DCCB',
          300: '#D8C9AE',
          400: '#C9A877',
          500: '#A9835A',
          600: '#8F6C46',
          700: '#6B5F52',
          800: '#4A4038',
          900: '#2B2420',
        },
        accent: {
          400: '#C9A877',
          500: '#A9835A',
          600: '#8F6C46',
        },
      },
      fontFamily: {
        sans: ['Inter', 'system-ui', 'sans-serif'],
        serif: ['Newsreader', 'ui-serif', 'serif'],
      },
      borderRadius: {
        '2xl': '1.25rem',
        '3xl': '1.625rem',
      },
      boxShadow: {
        card: '0 14px 34px rgba(43,36,32,0.10)',
        modal: '0 30px 70px rgba(43,36,32,0.25)',
        soft: '0 20px 50px rgba(43,36,32,0.06)',
      },
    },
  },
  plugins: [],
}
