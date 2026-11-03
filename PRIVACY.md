# Privacy policy

**WSLc Desktop Manager**

Last updated: November 2, 2026

## Summary

WSLc Desktop Manager is a local desktop application for managing WSLc containers. The project maintainers do not collect or store your personal data on developer-operated servers. The app does not require an app account and does not include advertising, tracking, usage analytics, or automatic diagnostic uploads to the maintainers.

## Data kept on your device

The app and WSLc use local data to provide the features you choose to use:

- App preferences, such as appearance, tray behavior, session resources, and remembered registry server addresses, are saved in `%LOCALAPPDATA%\WslcDesktop\settings.json`.
- WSLc session and container data are stored locally, including under `%LOCALAPPDATA%\WslcDesktop\Data`. Container images, volumes, logs, configuration, and files you mount into containers are managed by WSLc.
- If you use the WSLc settings editor, it reads and writes `%LOCALAPPDATA%\wslc\settings.yaml`.
- Container performance statistics are held temporarily in memory to display local CPU, memory, network, and disk charts. These statistics are not uploaded to the maintainers.

The maintainers do not receive this data through the app.

## Registry credentials and network activity

If you sign in to a container registry, the app passes your credentials to WSLc. WSLc manages their storage using its configured credential store. Passwords and tokens are not saved in the app's preferences file or sent to the project maintainers.

Operations such as signing in to a registry, pulling an image, or installing WSLc components can contact the services needed for that operation. Those services may receive information such as your IP address, requested image, and authentication information. Containers you run can also communicate over the network according to their configuration and behavior.

Microsoft Windows, the Microsoft Store, WSL/WSLc, container registries, and software running inside containers are separate products or services. Their data handling is governed by their own settings and privacy policies. This policy does not promise that those products or services collect no data.

## Sharing and sale of data

The app does not send personal data to the project maintainers for sale, advertising, profiling, or sharing with data brokers.

If you voluntarily submit a bug report, log excerpt, screenshot, or other information on GitHub, that information is handled through GitHub rather than automatically collected by the app. Public issues and attachments may be visible to others. Remove credentials, tokens, private container contents, and other sensitive information before posting.

## Retention and deletion

Local preferences and WSLc resources remain on your device until you change or remove them. Closing the app leaves its containers running; it does not delete container data. Uninstalling the app may also leave preferences, WSLc resources, registry credentials, or files outside the app package on your device.

Use the app or WSLc tools to stop and remove resources and sign out of registries. App preferences can be removed from the settings path listed above after closing the app. Back up any files or container data you want to keep before deleting them.

## Documentation website

The documentation website is hosted on GitHub Pages. Website access is subject to [GitHub's privacy statement](https://docs.github.com/en/site-policy/privacy-policies/github-general-privacy-statement). This is separate from the desktop app's local operation.

## Changes and contact

Changes to this policy will be published in this repository and on the documentation website, with an updated date.

For questions about this policy, contact the project maintainers through the [GitHub issue tracker](https://github.com/seiggy/wslc-desktop/issues). Do not post sensitive personal information or secrets in a public issue.
