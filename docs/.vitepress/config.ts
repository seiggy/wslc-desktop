import { defineConfig } from 'vitepress'

// GitHub project pages are served from /<repo>/. Override with DOCS_BASE for other hosts.
const base = process.env.DOCS_BASE ?? '/wslc-desktop/'

export default defineConfig({
  title: 'WSLc Desktop Manager',
  description: 'A native Windows desktop app for managing WSLc containers, images, volumes, networks and compose stacks.',
  base,
  cleanUrls: true,
  lastUpdated: true,
  head: [
    ['link', { rel: 'icon', type: 'image/svg+xml', href: `${base}logo.svg` }],
    ['meta', { name: 'theme-color', content: '#2563eb' }],
  ],
  themeConfig: {
    logo: '/logo.svg',
    nav: [
      { text: 'Getting started', link: '/getting-started' },
      { text: 'Features', link: '/features/containers' },
      { text: 'Compose', link: '/compose' },
      { text: 'Troubleshooting', link: '/troubleshooting' },
    ],
    sidebar: [
      {
        text: 'Introduction',
        items: [
          { text: 'Overview', link: '/' },
          { text: 'Getting started', link: '/getting-started' },
        ],
      },
      {
        text: 'Feature walkthroughs',
        items: [
          { text: 'Containers', link: '/features/containers' },
          { text: 'Container details', link: '/features/container-details' },
          { text: 'New container', link: '/features/new-container' },
          { text: 'Images', link: '/features/images' },
          { text: 'Volumes', link: '/features/volumes' },
          { text: 'Networks', link: '/features/networks' },
          { text: 'Stacks (Compose)', link: '/features/stacks' },
          { text: 'Secrets', link: '/features/secrets' },
          { text: 'Environment', link: '/features/environment' },
        ],
      },
      {
        text: 'Guides',
        items: [
          { text: 'Compose guide', link: '/compose' },
          { text: 'Settings and registries', link: '/settings' },
        ],
      },
      {
        text: 'Help',
        items: [
          { text: 'Troubleshooting', link: '/troubleshooting' },
          { text: 'FAQ', link: '/faq' },
          { text: 'Contributing', link: '/contributing' },
          { text: 'Privacy policy', link: '/privacy' },
        ],
      },
    ],
    socialLinks: [{ icon: 'github', link: 'https://github.com/seiggy/wslc-desktop' }],
    editLink: {
      pattern: 'https://github.com/seiggy/wslc-desktop/edit/main/docs/:path',
      text: 'Edit this page on GitHub',
    },
    search: { provider: 'local' },
    footer: { message: `Released under the MIT License. <a href="${base}privacy">Privacy policy</a>.` },
  },
})
